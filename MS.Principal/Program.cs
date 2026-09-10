using Foundation;
using Foundation.Keys;
using Foundation.Models;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

Console.Title = "MS.Principal";

// Set base paths and verify if keys exist
string solutionRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var keyManagement = new KeyManagement(solutionRootPath, "MS.Principal");
var signature = new SignatureService();

keyManagement.CheckKeys();

// Test sign and verify text
string text = "Test message";
string privateKeyPath = Path.Combine(solutionRootPath, "MS.Principal", "Keys", "MS.Principal.private.pem");
string publicKeyPath = Path.Combine(solutionRootPath, "MS.Principal", "Keys", "MS.Principal.public.pem");

string validateResult = signature.SignText(text, privateKeyPath);
Console.WriteLine(validateResult);

bool isVerified = signature.VerifySignature(text, validateResult, publicKeyPath);
Console.WriteLine($"Valid: {isVerified}");

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


while (true)
{
    Console.WriteLine("[MS.Principal] Operações:");
    Console.WriteLine("1. Criar novo pedido");
    Console.WriteLine("2. Listar pedidos");
    Console.WriteLine("3. Remover pedidos");
    Console.WriteLine("4. Consultar pedidos e status");
    Console.WriteLine("5. Sair");

    var mainMenuOption = Console.ReadLine();
    switch (mainMenuOption)
    {
        case "1":
        {
            Console.WriteLine("[MS.Principal] Criando novo pedido...");
            // Cria novo pedido
            var novoPedido = new PedidoCriado
            {
                ClienteId = "915d5e76-ab62-43ae-99c4-4d2075125cc9"
            };

            bool pedidoFinalizado = false;
            bool pedidoCancelado = false;

            while (!pedidoFinalizado && !pedidoCancelado)
            {
                Console.Write("ID do item: ");
                var itemId = Console.ReadLine()?.Trim();
                if (itemId is null)
                {
                    pedidoCancelado = true;
                    break;
                }

                if (string.IsNullOrWhiteSpace(itemId))
                {
                    Console.WriteLine("[MS.Principal] O ID do item não pode ser vazio.");
                    continue;
                }

                if (novoPedido.Itens.Any(item => item.Id == itemId))
                {
                    Console.WriteLine("[MS.Principal] Este item já foi adicionado ao pedido.");
                    continue;
                }

                int quantidade;
                while (true)
                {
                    Console.Write("Quantidade: ");
                    var quantidadeInformada = Console.ReadLine();
                    if (quantidadeInformada is null)
                    {
                        pedidoCancelado = true;
                        break;
                    }

                    if (int.TryParse(quantidadeInformada, out quantidade) && quantidade > 0)
                    {
                        novoPedido.Itens.Add(new ItemPedido { Id = itemId, Quantidade = quantidade });
                        Console.WriteLine($"[MS.Principal] Item {itemId} adicionado: {quantidade} unidade(s).");
                        break;
                    }

                    Console.WriteLine("[MS.Principal] Informe uma quantidade inteira maior que zero.");
                }

                if (pedidoCancelado)
                {
                    break;
                }

                bool adicionarOutro = false;
                while (!adicionarOutro && !pedidoFinalizado && !pedidoCancelado)
                {
                    Console.WriteLine("1. Adicionar outro item");
                    Console.WriteLine("2. Finalizar pedido");
                    Console.WriteLine("3. Cancelar e voltar");

                    switch (Console.ReadLine())
                    {
                        case "1":
                            adicionarOutro = true;
                            break;
                        case "2":
                            pedidoFinalizado = true;
                            break;
                        case "3":
                        case null:
                            pedidoCancelado = true;
                            break;
                        default:
                            Console.WriteLine("[MS.Principal] Opção inválida. Tente novamente.");
                            break;
                    }
                }
            }

            if (pedidoCancelado)
            {
                Console.WriteLine("[MS.Principal] Criação do pedido cancelada.");
                break;
            }

            // Assina o pedido
            string pedidoJson = JsonSerializer.Serialize(novoPedido);
            var signed = signature.SignText(pedidoJson, privateKeyPath);

            // Cria Mensagem
            var eventMessage = new Message<PedidoCriado>
            {
                Producer = "MS.Principal",
                Content = novoPedido,
                Signature = signed,
            };

            // Serializa mensagem
            string jsonMensagem = JsonSerializer.Serialize(eventMessage);
            byte[] body = Encoding.UTF8.GetBytes(jsonMensagem);

            // Envia mensagem ao evento
            await channel.BasicPublishAsync(
                exchange: "eCommerce",
                routingKey: "pedido.criado",
                body: body
            );

            Console.WriteLine($"Mensagem enviada:\n{jsonMensagem}");
            break;
        }
        case "2":
            Console.WriteLine("[MS.Principal] Listing orders...");
            break;
        case "3":
            Console.WriteLine("[MS.Principal] Removing orders...");
            break;
        case "4":
            Console.WriteLine("[MS.Principal] Consulting orders and status...");
            break;
        case "5":
        case null:
            Console.WriteLine("[MS.Principal] Saindo...");
            return;
        default:
            Console.WriteLine("[MS.Principal] Invalid option. Please try again.");
            break;
    }
}
