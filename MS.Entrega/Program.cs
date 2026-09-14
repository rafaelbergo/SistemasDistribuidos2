using Foundation;
using Foundation.Keys;
using Foundation.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

Console.Title = "MS.Entrega";

// Set base paths and verify if keys exist
string solutionRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
string privateKeyPath = Path.Combine(solutionRootPath, "MS.Entrega", "Keys", "MS.Entrega.private.pem");

var keyManagement = new KeyManagement(solutionRootPath, "MS.Entrega");
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

// Create Queue
string queueName = "fila_entrega";
await channel.QueueDeclareAsync(
    queue: queueName,
    durable: true,
    exclusive: false,
    autoDelete: false
);

// Bind queue
await channel.QueueBindAsync(
    queue: queueName,
    exchange: "eCommerce",
    routingKey: "pagamento.aprovado"
);

// Configure async consumer
var consumer = new AsyncEventingBasicConsumer(channel);

consumer.ReceivedAsync += async (model, ea) =>
{
    string routingKey = ea.RoutingKey;
    byte[] body = ea.Body.ToArray();
    string jsonReceiverMessage = Encoding.UTF8.GetString(body);

    Console.WriteLine($"\n[MS.Entrega] Message received by routing key {routingKey}");

    var eventMessage = JsonSerializer.Deserialize<Message<PedidoCriado>>(jsonReceiverMessage);
    if (eventMessage == null || eventMessage.Content == null)
    {
        Console.WriteLine("[MS.Entrega] Error on desserialize message.");
        return;
    }

    // Check message signature
    string producerPublicKeyPath = Path.Combine(solutionRootPath, "MS.Entrega", "Keys", $"{eventMessage.Producer}.public.pem");

    if (!File.Exists(producerPublicKeyPath))
    {
        Console.WriteLine($"[MS.Entrega] Producer public key for {eventMessage.Producer} not found in: {producerPublicKeyPath}");
        return;
    }

    string contentJson = JsonSerializer.Serialize(eventMessage.Content);
    bool isValid = signatureService.VerifySignature(contentJson, eventMessage.Signature, producerPublicKeyPath);
    if (!isValid)
    {
        Console.WriteLine($"[MS.Entrega] Message discarded: invalid signature for producer '{eventMessage.Producer}'");
        return;
    }

    Console.WriteLine($"[MS.Entrega] Valid signature from {eventMessage.Producer} for Order ID: {eventMessage.Content.Id}");

    // Check routing key
    if (routingKey == "pagamento.aprovado")
    {
        Console.WriteLine($"[MS.Entrega] Creating invoice and preparing delivery for OrderID: {eventMessage.Content.Id}...");
        await Task.Delay(1000);

        Console.WriteLine($"[MS.Entrega] Invoice created and products shipped for delivery of Order {eventMessage.Content.Id}.");
        await PublishEventAsync(channel, "pedido.enviado", eventMessage.Content, signatureService, privateKeyPath);
    }

    await Task.CompletedTask;
};

await channel.BasicConsumeAsync(
    queue: queueName,
    autoAck: true,
    consumer: consumer
);

Console.ReadLine();

async Task PublishEventAsync(IChannel channel, string routingKey, PedidoCriado pedido, SignatureService signatureService, string privateKeyPath)
{
    string responseJson = JsonSerializer.Serialize(pedido);
    string signature = signatureService.SignText(responseJson, privateKeyPath);

    var responseMessage = new Message<PedidoCriado>
    {
        Producer = "MS.Entrega",
        Content = pedido,
        Signature = signature
    };

    byte[] body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(responseMessage));

    await channel.BasicPublishAsync(
        exchange: "eCommerce",
        routingKey: routingKey,
        body: body
    );

    Console.WriteLine($"[MS.Entrega] Event {routingKey} published successfully for Order {pedido.Id}");
}