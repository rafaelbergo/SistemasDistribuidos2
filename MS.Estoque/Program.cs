using Foundation;
using Foundation.Keys;
using Foundation.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

Console.Title = "MS.Estoque";

// Set base paths and verify if keys exist
string solutionRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
string privateKeyPath = Path.Combine(solutionRootPath, "MS.Estoque", "Keys", "MS.Estoque.private.pem");

var keyManagement = new KeyManagement(solutionRootPath, "MS.Estoque");
keyManagement.CheckKeys();
var signatureService = new SignatureService();

// Set products quantity
var inventory = new Dictionary<string, int>
{
    { "A", 10 },
    { "B", 5 },
    { "C", 0 },
    { "D", 1 },
};

// Keep the original reservation so rejection/deletion restores it only once.
var reservations = new Dictionary<string, List<ItemPedido>>();
var processedOrders = new HashSet<string>();

// RabbitMQ Connection
var factory = new ConnectionFactory { HostName = "localhost" };
using var connection = await factory.CreateConnectionAsync();
using var channel = await connection.CreateChannelAsync();

// Create Exchange
await channel.ExchangeDeclareAsync(
    exchange: "eCommerce",
    type: ExchangeType.Direct,
    durable: true
);

// Create Queue
string queueName = "fila_estoque";
await channel.QueueDeclareAsync(
    queue: queueName,
    durable: true,
    exclusive: false,
    autoDelete: false
);

// Bind queues
await channel.QueueBindAsync(
    queue: queueName,
    exchange: "eCommerce",
    routingKey: "pedido.criado"
);

await channel.QueueBindAsync(
    queue: queueName,
    exchange: "eCommerce",
    routingKey: "pedido.excluido"
);

// Configure async consumer
await channel.QueueBindAsync(
    queue: queueName,
    exchange: "eCommerce",
    routingKey: "pagamento.recusado"
);

var consumer = new AsyncEventingBasicConsumer(channel);

await channel.QueueBindAsync(queueName, "eCommerce", "produtos.consultar");

consumer.ReceivedAsync += async (model, ea) =>
{
    string routingKey = ea.RoutingKey;
    byte[] body = ea.Body.ToArray();
    string jsonReceiverMessage = Encoding.UTF8.GetString(body);

    Console.WriteLine($"[MS.Estoque] Message rececived by {routingKey}");

    if (routingKey == "produtos.consultar")
    {
        try
        {
            var request = JsonSerializer.Deserialize<Message<ConsultaProdutos>>(jsonReceiverMessage);
            string principalKey = Path.Combine(solutionRootPath, "MS.Estoque", "Keys", "MS.Principal.public.pem");
            if (request?.Producer != "MS.Principal" || request.Content is null ||
                !Guid.TryParseExact(request.Content.Id, "N", out _) ||
                !signatureService.VerifySignature(JsonSerializer.Serialize(request.Content), request.Signature, principalKey))
            {
                Console.WriteLine("[MS.Estoque] Consulta de produtos inválida; descartada.");
                return;
            }

            // This callback also handles reservations, so the snapshot reflects their current balance.
            var catalogo = new CatalogoProdutos
            {
                ConsultaId = request.Content.Id,
                Produtos = inventory.OrderBy(item => item.Key).Select(item => new ProdutoDisponivel
                {
                    Id = item.Key,
                    Descricao = $"Produto {item.Key}",
                    QuantidadeDisponivel = item.Value
                }).ToList()
            };
            await PublishEventAsync(channel, $"produtos.listados.{request.Content.Id}", catalogo, signatureService, privateKeyPath);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or System.Security.Cryptography.CryptographicException
            or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.WriteLine($"[MS.Estoque] Consulta de produtos descartada: {ex.Message}");
        }
        return;
    }

    var eventMessage = JsonSerializer.Deserialize<Message<PedidoCriado>>(jsonReceiverMessage);
    if (eventMessage == null || eventMessage.Content == null)
    {
        Console.WriteLine("[MS.Estoque] Error on desserialize message");
        return;
    }

    // Check message signature
    string producerPublicKeyPath = Path.Combine(solutionRootPath, "MS.Estoque", "Keys", $"{eventMessage.Producer}.public.pem");

    if (!File.Exists(producerPublicKeyPath))
    {
        Console.WriteLine($"[MS.Estoque] Public key not found for {eventMessage.Producer} in: {producerPublicKeyPath}");
        return;
    }

    string contentJson = JsonSerializer.Serialize(eventMessage.Content);
    bool isValid = signatureService.VerifySignature(contentJson, eventMessage.Signature, producerPublicKeyPath);

    if (!isValid)
    {
        Console.WriteLine("[MS.Estoque] Message received with invalid signature");
        return;
    }

    Console.WriteLine($"[MS.Estoque] Signature valid for {eventMessage.Producer}");
    /*
    Console.WriteLine($"[MS.Estoque] Id: {eventMessage.Content.Id}, ClientId: {eventMessage.Content.ClienteId}");
    foreach (var item in eventMessage.Content.Itens)
    {
        Console.WriteLine($"  ItemId: {item.Id}, Quantity: {item.Quantidade}");
    }*/

    // Check routing key
    if (routingKey == "pedido.criado")
    {
        if (!processedOrders.Add(eventMessage.Content.Id))
        {
            Console.WriteLine($"[MS.Estoque] Order {eventMessage.Content.Id} already processed; ignoring duplicate.");
            return;
        }

        bool itemsAvailable = true;

        // Check if all items are available
        foreach (var item in eventMessage.Content.Itens)
        {
            if (!inventory.ContainsKey(item.Id) || inventory[item.Id] < item.Quantidade)
            {
                itemsAvailable = false;
                Console.WriteLine($"[MS.Estoque] Insufficient stock or item not found for ItemId '{item.Id}'");
                break;
            }
        }

        if (itemsAvailable)
        {
            // Remove items from stock
            foreach (var item in eventMessage.Content.Itens)
            {
                inventory[item.Id] -= item.Quantidade;
                Console.WriteLine($"[MS.Estoque] Removed {item.Quantidade} of Item {item.Id}. Remaining: {inventory[item.Id]}");
            }

            reservations[eventMessage.Content.Id] = eventMessage.Content.Itens;

            // Publish success event
            await PublishEventAsync(channel, "pedido.estoque_ok", eventMessage.Content, signatureService, privateKeyPath);
        }
        else
        {
            Console.WriteLine($"[MS.Estoque] Order {eventMessage.Content.Id} cancelled, items not available");

            // Publish failure event
            await PublishEventAsync(channel, "estoque.indisponivel", eventMessage.Content, signatureService, privateKeyPath);
        }
    }

    else if (routingKey is "pedido.excluido")
    {
        if (!reservations.Remove(eventMessage.Content.Id, out var reservedItems))
        {
            Console.WriteLine($"[MS.Estoque] No reservation to restore for Order {eventMessage.Content.Id}; event '{routingKey}' ignored.");
            return;
        }

        // Restore items to inventory
        foreach (var item in reservedItems)
        {
            if (inventory.ContainsKey(item.Id))
            {
                inventory[item.Id] += item.Quantidade;
                Console.WriteLine($"[MS.Estoque] Restored {item.Quantidade} for Item {item.Id}. Available: {inventory[item.Id]}. Order: {eventMessage.Content.Id}, event: {routingKey}");
            }
        }
    }

    await Task.CompletedTask;
};

await channel.BasicConsumeAsync(
    queue: queueName,
    autoAck: true,
    consumer: consumer
);

Console.ReadLine();


async Task PublishEventAsync<T>(IChannel channel, string routingKey, T pedido, SignatureService signatureService, string privateKeyPath)
{
    string responseJson = JsonSerializer.Serialize(pedido);
    string signature = signatureService.SignText(responseJson, privateKeyPath);
    
    var responseMessage = new Message<T>
    {
        Producer = "MS.Estoque",
        Content = pedido,
        Signature = signature
    };

    byte[] body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(responseMessage));

    await channel.BasicPublishAsync(
        exchange: "eCommerce",
        routingKey: routingKey,
        body: body
    );

    Console.WriteLine($"[MS.Estoque] Event '{routingKey}' published");
}
