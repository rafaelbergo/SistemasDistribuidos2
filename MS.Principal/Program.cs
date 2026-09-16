using Foundation;
using Foundation.Keys;
using Foundation.Models;
using MS.Principal;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

Console.Title = "MS.Principal";
Console.OutputEncoding = Encoding.UTF8;
string solutionRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
string dataPath = Environment.GetEnvironmentVariable("MS_PRINCIPAL_DATA_PATH")
    ?? Path.Combine(solutionRootPath, "MS.Principal", "Data", "pedidos.json");
dataPath = Path.GetFullPath(dataPath);
Directory.CreateDirectory(Path.GetDirectoryName(dataPath)!);
// Prevent two Principal instances from overwriting the same database.
using var dataLock = new FileStream(dataPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
var orders = new PedidoRepository(dataPath);
Console.WriteLine($"[MS.Principal] Pedidos carregados: {orders.Listar().Count}. Arquivo: {dataPath}");

var keyManagement = new KeyManagement(solutionRootPath, "MS.Principal");
keyManagement.CheckKeys();
var signature = new SignatureService();
string privateKeyPath = Path.Combine(solutionRootPath, "MS.Principal", "Keys", "MS.Principal.private.pem");

var factory = new ConnectionFactory { HostName = "localhost" };
using var connection = await factory.CreateConnectionAsync();
using var publisherChannel = await connection.CreateChannelAsync(new CreateChannelOptions(
    publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));
await publisherChannel.ExchangeDeclareAsync("eCommerce", ExchangeType.Direct, durable: true);
using var consumerChannel = await connection.CreateChannelAsync();
const string queueName = "fila_principal";
await consumerChannel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false);
string[] orderEvents =
[
    "pagamento.aprovado", "pagamento.recusado", "pedido.enviado",
    "pedido.estoque_ok", "estoque.indisponivel"
];
foreach (string routingKey in orderEvents)
    await consumerChannel.QueueBindAsync(queueName, "eCommerce", routingKey);
await consumerChannel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false);

var consumer = new AsyncEventingBasicConsumer(consumerChannel);
consumer.ReceivedAsync += async (_, ea) =>
{
    try
    {
        var message = JsonSerializer.Deserialize<Message<PedidoCriado>>(ea.Body.Span);
        if (message?.Content is null || string.IsNullOrWhiteSpace(message.Content.Id) ||
            string.IsNullOrWhiteSpace(message.MessageId))
            throw new JsonException("Pedido ou ID de mensagem ausente.");
        string? expectedProducer = ea.RoutingKey switch
        {
            "pagamento.aprovado" or "pagamento.recusado" => "MS.Pagamento",
            "pedido.enviado" => "MS.Entrega",
            "pedido.estoque_ok" or "estoque.indisponivel" => "MS.Estoque",
            _ => null
        };
        if (expectedProducer is null || message.Producer != expectedProducer)
            throw new JsonException($"Produtor inesperado para '{ea.RoutingKey}'.");
        string producerKey = Path.Combine(solutionRootPath, "MS.Principal", "Keys", $"{expectedProducer}.public.pem");
        if (!signature.VerifySignature(JsonSerializer.Serialize(message.Content), message.Signature, producerKey))
            throw new CryptographicException("Assinatura inválida.");

        string status = orders.AplicarEvento(ea.RoutingKey, message);
        Console.WriteLine($"\n[MS.Principal] Pedido {message.Content.Id}: {status}. Evento: {ea.RoutingKey}");
    }
    catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException or ArgumentException)
    {
        Console.WriteLine($"[MS.Principal] Evento descartado: {ex.Message}");
        await consumerChannel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
        return;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.WriteLine($"[MS.Principal] Falha ao ler a chave ou salvar o pedido; evento será reprocessado: {ex.Message}");
        await Task.Delay(1000);
        await consumerChannel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
        return;
    }
    // Acknowledge only after the updated order and event history reach disk.
    await consumerChannel.BasicAckAsync(ea.DeliveryTag, multiple: false);
};
string consumerTag = await consumerChannel.BasicConsumeAsync(queueName, autoAck: false, consumer: consumer);
using var stopping = new CancellationTokenSource();
Task publisher = Task.Run(() => PublicarPendenciasAsync(stopping.Token));

try
{
    bool running = true;
    while (running)
    {
        Console.WriteLine("\n[MS.Principal] PEDIDOS");
        Console.WriteLine("1. Criar pedido");
        Console.WriteLine("2. Listar pedidos e status");
        Console.WriteLine("3. Consultar pedido e histórico");
        Console.WriteLine("4. Visualizar produtos");
        if (orders.Listar().Any(p => p.Status == "rascunho"))
            Console.WriteLine("5. Enviar rascunho salvo anteriormente");
        Console.WriteLine("0. Sair");
        Console.Write("Opção: ");
        try
        {
            switch (Console.ReadLine()?.Trim())
            {
                case "1":
                    var novo = LerPedido();
                    if (novo is null) break;
                    var criado = orders.Criar(novo, enviar: true);
                    Console.WriteLine($"[MS.Principal] Pedido salvo: {criado.Pedido.Id}. Status: {criado.Status}. Aguardando processamento.");
                    break;
                case "2":
                    Listar(orders.Listar());
                    break;
                case "3":
                    var consultado = Selecionar();
                    if (consultado is not null) Exibir(consultado);
                    break;
                case "4":
                    Console.WriteLine("[MS.Principal] Consultando produtos...");
                    var catalogo = await ConsultaProdutosClient.ConsultarAsync(connection, privateKeyPath,
                        Path.Combine(solutionRootPath, "MS.Principal", "Keys", "MS.Estoque.public.pem"));
                    Console.WriteLine("ID | Descrição | Quantidade disponível");
                    foreach (var produto in catalogo.Produtos)
                        Console.WriteLine($"{produto.Id} | {produto.Descricao} | {produto.QuantidadeDisponivel}");
                    if (catalogo.Produtos.Count == 0) Console.WriteLine("Nenhum produto cadastrado.");
                    Console.WriteLine("Use esses IDs ao criar o pedido. A disponibilidade será verificada novamente no processamento.");
                    break;
                case "5":
                    var envio = Selecionar();
                    if (envio is not null) Enviar(envio.Pedido.Id);
                    break;
                case "0":
                case null:
                    running = false;
                    break;
                default:
                    Console.WriteLine("[MS.Principal] Opção inválida.");
                    break;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException)
        {
            Console.WriteLine($"[MS.Principal] Operação não concluída: {ex.Message}");
        }
    }
}
finally
{
    stopping.Cancel();
    await publisher;
    await consumerChannel.BasicCancelAsync(consumerTag);
}
Console.WriteLine("[MS.Principal] Pedidos salvos. Saindo...");

void Enviar(string id)
{
    orders.Enviar(id);
    Console.WriteLine($"[MS.Principal] Pedido {id} salvo para envio. A publicação será tentada automaticamente até ser confirmada.");
}

PedidoRegistro? Selecionar()
{
    Console.Write("ID completo do pedido (Enter para voltar): ");
    string? id = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(id)) return null;
    return orders.Consultar(id) ?? throw new InvalidOperationException("Pedido não encontrado.");
}

void Listar(List<PedidoRegistro> registros)
{
    if (registros.Count == 0) Console.WriteLine("[MS.Principal] Nenhum pedido encontrado.");
    var pendentes = orders.PublicacoesPendentes().Select(m => m.Content.Id).ToHashSet();
    foreach (var registro in registros)
    {
        Console.WriteLine($"{registro.Pedido.Id} | Cliente: {registro.Pedido.ClienteId} | Status: {registro.Status}" +
            (pendentes.Contains(registro.Pedido.Id) ? " | Publicação pendente" : "") +
            (registro.Excluido ? " | Excluído" : ""));
    }
}

void Exibir(PedidoRegistro registro)
{
    Listar([registro]);
    Console.WriteLine($"Criado: {registro.CriadoEm.ToLocalTime():g} | Atualizado: {registro.AtualizadoEm.ToLocalTime():g}");
    foreach (var item in registro.Pedido.Itens)
        Console.WriteLine($"  Item {item.Id}: {item.Quantidade} unidade(s)");
    Console.WriteLine("Histórico:");
    foreach (var evento in registro.Historico)
        Console.WriteLine($"  {evento.RecebidoEm.ToLocalTime():G} | {evento.Evento} | {evento.Produtor}" +
            (evento.Aplicado ? "" : " | Não alterou o status"));
}

PedidoCriado? LerPedido()
{
    Console.WriteLine("Digite /cancelar para voltar sem salvar.");
    var pedido = new PedidoCriado();
    while (true)
    {
        Console.Write("ID do cliente: ");
        string? cliente = Console.ReadLine()?.Trim();
        if (cliente is null or "/cancelar") return null;
        pedido.ClienteId = cliente;
        if (!string.IsNullOrWhiteSpace(pedido.ClienteId)) break;
        Console.WriteLine("Informe o ID do cliente.");
    }
    while (true)
    {
        Console.Write("ID do item (Enter finaliza a lista): ");
        string? id = Console.ReadLine()?.Trim();
        if (id is null or "/cancelar") return null;
        if (id.Length == 0)
        {
            if (pedido.Itens.Count > 0) return pedido;
            Console.WriteLine("Adicione pelo menos um item.");
            continue;
        }
        if (pedido.Itens.Any(i => i.Id == id))
        {
            Console.WriteLine("Item já adicionado.");
            continue;
        }
        while (true)
        {
            Console.Write("Quantidade: ");
            string? quantidade = Console.ReadLine()?.Trim();
            if (quantidade is null or "/cancelar") return null;
            if (int.TryParse(quantidade, out int valor) && valor > 0)
            {
                pedido.Itens.Add(new ItemPedido { Id = id, Quantidade = valor });
                break;
            }
            Console.WriteLine("Informe uma quantidade inteira maior que zero.");
        }
    }
}

async Task PublicarPendenciasAsync(CancellationToken token)
{
    while (!token.IsCancellationRequested)
    {
        try
        {
            foreach (var mensagem in orders.PublicacoesPendentes())
            {
                token.ThrowIfCancellationRequested();
                mensagem.Signature = signature.SignText(JsonSerializer.Serialize(mensagem.Content), privateKeyPath);
                var properties = new BasicProperties
                {
                    Persistent = true,
                    ContentType = "application/json",
                    MessageId = mensagem.MessageId
                };
                await publisherChannel.BasicPublishAsync("eCommerce", "pedido.criado", mandatory: true,
                    basicProperties: properties, body: Encoding.UTF8.GetBytes(JsonSerializer.Serialize(mensagem)),
                    cancellationToken: token);
                orders.ConfirmarPublicacao(mensagem.MessageId);
                Console.WriteLine($"\n[MS.Principal] Evento pedido.criado confirmado pelo RabbitMQ para {mensagem.Content.Id}.");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
        catch (Exception ex)
        {
            Console.WriteLine($"[MS.Principal] Publicação pendente; nova tentativa em 3 segundos: {ex.Message}");
        }
        try { await Task.Delay(3000, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
    }
}
