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
var keyManagement = new KeyManagement(solutionRootPath, "MS.Estoque");

keyManagement.CheckKeys();
var signatureService = new SignatureService();

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

// Create Queue and Bind to Exchange
string queueName = "fila_estoque";
await channel.QueueDeclareAsync(
    queue: queueName,
    durable: true,
    exclusive: false,
    autoDelete: false
);

await channel.QueueBindAsync(
    queue: queueName,
    exchange: "eCommerce",
    routingKey: "pedido.criado"
);

// Configure async consumer
var consumer = new AsyncEventingBasicConsumer(channel);

consumer.ReceivedAsync += async (model, ea) =>
{
    string routingKey = ea.RoutingKey;
    byte[] body = ea.Body.ToArray();
    string jsonReceiverMessage = Encoding.UTF8.GetString(body);

    Console.WriteLine($"[MS.Estoque] Message rececived by {routingKey}");

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

    // Check items
    Console.WriteLine($"[MS.Estoque] Id: {eventMessage.Content.Id}, ClientId: {eventMessage.Content.ClienteId}");
    foreach (var item in eventMessage.Content.Itens)
    {
        Console.WriteLine($"  ItemId: {item.Id}, Quantity: {item.Quantidade}");
    }

    await Task.CompletedTask;
};

await channel.BasicConsumeAsync(
    queue: queueName,
    autoAck: true,
    consumer: consumer
);

Console.ReadLine();