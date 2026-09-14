using Foundation;
using Foundation.Keys;
using Foundation.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

Console.Title = "MS.Pagamento";

// Set base paths and verify if keys exist
string solutionRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
string privateKeyPath = Path.Combine(solutionRootPath, "MS.Pagamento", "Keys", "MS.Pagamento.private.pem");

var keyManagement = new KeyManagement(solutionRootPath, "MS.Pagamento");
keyManagement.CheckKeys();
var signatureService = new SignatureService();

int processPaymentChance = 70;

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
string queueName = "fila_pagamento";
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
    routingKey: "pedido.estoque_ok"
);

// Configure async consumer
var consumer = new AsyncEventingBasicConsumer(channel);

consumer.ReceivedAsync += async (model, ea) =>
{
    string routingKey = ea.RoutingKey;
    byte[] body = ea.Body.ToArray();
    string jsonReceiverMessage = Encoding.UTF8.GetString(body);

    Console.WriteLine($"\n[MS.Pagamento] Message received by routing key '{routingKey}'");

    var eventMessage = JsonSerializer.Deserialize<Message<PedidoCriado>>(jsonReceiverMessage);
    if (eventMessage == null || eventMessage.Content == null)
    {
        Console.WriteLine("[MS.Pagamento] Error on desserialize message.");
        return;
    }

    // Check message signature
    string producerPublicKeyPath = Path.Combine(solutionRootPath, "MS.Pagamento", "Keys", $"{eventMessage.Producer}.public.pem");
    if (!File.Exists(producerPublicKeyPath))
    {
        Console.WriteLine($"[MS.Pagamento] Producer public key {eventMessage.Producer} not found on {producerPublicKeyPath}");
        return;
    }

    string contentJson = JsonSerializer.Serialize(eventMessage.Content);
    bool isValid = signatureService.VerifySignature(contentJson, eventMessage.Signature, producerPublicKeyPath);

    if (!isValid)
    {
        Console.WriteLine($"[MS.Pagamento] Signature is invalid to producer {eventMessage.Producer}");
        return;
    }

    Console.WriteLine($"[MS.Pagamento] Valid signature from {eventMessage.Producer} for Order ID: {eventMessage.Content.Id}");

    // Check routing key
    if (routingKey == "pedido.estoque_ok")
    {
        Console.WriteLine($"[MS.Pagamento] Processing payment for OrderId: {eventMessage.Content.Id}...");

        // Check if payment is approved or not
        bool approved = Random.Shared.Next(100) < processPaymentChance;

        if (approved)
        {
            Console.WriteLine($"[MS.Pagamento] Payment approved for OrderId: {eventMessage.Content.Id}");
            await PublishEventAsync(channel, "pagamento.aprovado", eventMessage.Content, signatureService, privateKeyPath);
        }
        else
        {
            Console.WriteLine($"[MS.Pagamento] Payment rejected for OrderId: {eventMessage.Content.Id}");
            await PublishEventAsync(channel, "pagamento.recusado", eventMessage.Content, signatureService, privateKeyPath);
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

async Task PublishEventAsync(IChannel channel, string routingKey, PedidoCriado pedido, SignatureService signatureService, string privateKeyPath)
{
    string responseJson = JsonSerializer.Serialize(pedido);
    string signature = signatureService.SignText(responseJson, privateKeyPath);

    var responseMessage = new Message<PedidoCriado>
    {
        Producer = "MS.Pagamento",
        Content = pedido,
        Signature = signature
    };

    byte[] body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(responseMessage));

    await channel.BasicPublishAsync(
        exchange: "eCommerce",
        routingKey: routingKey,
        body: body
    );

    Console.WriteLine($"[MS.Pagamento] Event published successfully for Order {pedido.Id}:{routingKey}");
}