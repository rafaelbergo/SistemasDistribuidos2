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

/*
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
            Console.WriteLine("[MS.Principal] Creating a new order...");
            break;
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
            Console.WriteLine("[MS.Principal] Saindo...");
            return;
        default:
            Console.WriteLine("[MS.Principal] Invalid option. Please try again.");
            break;
    }
}*/



// Cria pedido teste
var novoPedido = new PedidoCriado
{
    ClienteId = "915d5e76-ab62-43ae-99c4-4d2075125cc9",
    Itens =
    [
        new ItemPedido { Id = Guid.NewGuid().ToString(), Quantidade = 1 },
        new ItemPedido { Id = Guid.NewGuid().ToString(), Quantidade = 10 }
    ]
};

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
Console.ReadLine();