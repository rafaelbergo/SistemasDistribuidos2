using Foundation;
using Foundation.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MS.Principal;

public static class ConsultaProdutosClient
{
    public static async Task<CatalogoProdutos> ConsultarAsync(
        IConnection connection, string privateKeyPath, string estoquePublicKeyPath)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            // A separate channel avoids concurrent publishing with the order publisher.
            using var channel = await connection.CreateChannelAsync(cancellationToken: timeout.Token);
            var consulta = new ConsultaProdutos();
            var signature = new SignatureService();
            var result = new TaskCompletionSource<CatalogoProdutos>(TaskCreationOptions.RunContinuationsAsynchronously);
            var queue = await channel.QueueDeclareAsync(queue: "", durable: false, exclusive: true,
                autoDelete: true, cancellationToken: timeout.Token);
            await channel.QueueBindAsync(queue.QueueName, "eCommerce", $"produtos.listados.{consulta.Id}",
                cancellationToken: timeout.Token);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, ea) =>
            {
                try
                {
                    var message = JsonSerializer.Deserialize<Message<CatalogoProdutos>>(ea.Body.Span);
                    if (message?.Producer == "MS.Estoque" && message.Content?.ConsultaId == consulta.Id &&
                        message.Content.Produtos is not null &&
                        signature.VerifySignature(JsonSerializer.Serialize(message.Content), message.Signature, estoquePublicKeyPath))
                        result.TrySetResult(message.Content);
                }
                catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException
                    or IOException or UnauthorizedAccessException or ArgumentException)
                {
                    Console.WriteLine($"[MS.Principal] Resposta de produtos descartada: {ex.Message}");
                }
                return Task.CompletedTask;
            };
            await channel.BasicConsumeAsync(queue.QueueName, autoAck: true, consumer: consumer,
                cancellationToken: timeout.Token);

            var request = new Message<ConsultaProdutos>
            {
                Producer = "MS.Principal",
                Content = consulta,
                Signature = signature.SignText(JsonSerializer.Serialize(consulta), privateKeyPath)
            };
            // An expired query must not accumulate while Estoque is stopped.
            await channel.BasicPublishAsync("eCommerce", "produtos.consultar", mandatory: false,
                basicProperties: new BasicProperties { ContentType = "application/json", Expiration = "5000" },
                body: Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)), cancellationToken: timeout.Token);
            return await result.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException("Estoque indisponível: nenhuma resposta válida em 5 segundos. Tente novamente.");
        }
    }
}
