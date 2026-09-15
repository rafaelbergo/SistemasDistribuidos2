using Foundation;
using Foundation.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Security.Cryptography;
using System.Text.Json;

Console.Title = "Consumidor.C2";
Console.OutputEncoding = System.Text.Encoding.UTF8;
const string consumerName = "Consumidor.C2";
const string queueName = "fila_promocoes_c2";
const string exchangeName = "Promoções";
string[] routingKeys = ["promocao.categoria.*"];

// The publisher provisions this local public key; no microservice is called.
string solutionRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
string publicKeyPath = Path.Combine(solutionRootPath, consumerName, "Keys", "MS.Promocoes.public.pem");
var signatureService = new SignatureService();

var factory = new ConnectionFactory { HostName = "localhost" };
using var connection = await factory.CreateConnectionAsync();
using var channel = await connection.CreateChannelAsync();

await channel.ExchangeDeclareAsync(exchangeName, ExchangeType.Topic, durable: true);
await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false);
foreach (string routingKey in routingKeys)
    await channel.QueueBindAsync(queueName, exchangeName, routingKey);

await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false);
var consumer = new AsyncEventingBasicConsumer(channel);
consumer.ReceivedAsync += async (_, ea) =>
{
    try
    {
        var message = JsonSerializer.Deserialize<Message<Promocao>>(ea.Body.Span);
        if (message?.Content is null || message.Producer != "MS.Promocoes")
            throw new JsonException("Produtor inválido ou promoção ausente.");

        var promotion = message.Content;
        if (string.IsNullOrWhiteSpace(promotion.Categoria) ||
            ea.RoutingKey != $"promocao.categoria.{promotion.Categoria}")
            throw new JsonException("Categoria incompatível com a routing key.");

        if (!File.Exists(publicKeyPath))
            throw new IOException($"Chave pública ausente em {publicKeyPath}. Inicie o MS.Promocoes atualizado para distribuí-la.");

        if (!signatureService.VerifySignature(JsonSerializer.Serialize(promotion), message.Signature, publicKeyPath))
            throw new CryptographicException("Assinatura inválida.");

        Console.WriteLine($"[{consumerName}] Promoção {promotion.Id} recebida por '{ea.RoutingKey}'");
        Console.WriteLine($"  Produto: {promotion.Produto} | Categoria: {promotion.Categoria} | Desconto: {promotion.DescontoPercentual}%");
        Console.WriteLine($"  Descrição: {promotion.Descricao}");

        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
    }
    catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException or IOException or ArgumentException)
    {
        Console.WriteLine($"[{consumerName}] Promoção descartada: {ex.Message}");
        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
    }
};

string consumerTag = await channel.BasicConsumeAsync(queueName, autoAck: false, consumer: consumer);
Console.WriteLine($"[{consumerName}] Aguardando promoções. Fila: {queueName}. Interesses: {string.Join(", ", routingKeys)}");
Console.WriteLine($"[{consumerName}] Pressione Enter para sair.");
Console.ReadLine();
await channel.BasicCancelAsync(consumerTag);
