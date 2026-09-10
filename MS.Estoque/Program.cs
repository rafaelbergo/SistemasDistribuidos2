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
    /*
    Console.WriteLine($"[MS.Estoque] Id: {eventMessage.Content.Id}, ClientId: {eventMessage.Content.ClienteId}");
    foreach (var item in eventMessage.Content.Itens)
    {
        Console.WriteLine($"  ItemId: {item.Id}, Quantity: {item.Quantidade}");
    }*/

    // Check routing key
    if (routingKey == "pedido.criado")
    {
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

    else if (routingKey == "pedido.excluido")
    {
        // Restore items to inventory
        foreach (var item in eventMessage.Content.Itens)
        {
            if (inventory.ContainsKey(item.Id))
            {
                inventory[item.Id] += item.Quantidade;
                Console.WriteLine($"[MS.Estoque] Restored {item.Quantidade} for Item {item.Id}");
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


async Task PublishEventAsync(IChannel channel, string routingKey, PedidoCriado pedido, SignatureService signatureService, string privateKeyPath)
{
    string responseJson = JsonSerializer.Serialize(pedido);
    string signature = signatureService.SignText(responseJson, privateKeyPath);
    
    var responseMessage = new Message<PedidoCriado>
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