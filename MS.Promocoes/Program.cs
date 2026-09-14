using Foundation;
using Foundation.Keys;
using Foundation.Models;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

Console.Title = "MS.Promocoes";

// Set base paths and verify if keys exist
string solutionRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
string privateKeyPath = Path.Combine(solutionRootPath, "MS.Promocoes", "Keys", "MS.Promocoes.private.pem");

var keyManagement = new KeyManagement(solutionRootPath, "MS.Promocoes");
keyManagement.CheckKeys();
var signatureService = new SignatureService();

// RabbitMQ Connection
var factory = new ConnectionFactory { HostName = "localhost" };
using var connection = await factory.CreateConnectionAsync();
using var channel = await connection.CreateChannelAsync();

// Create Topic Exchange for Promocoes
await channel.ExchangeDeclareAsync(
    exchange: "Promoções",
    type: ExchangeType.Topic,
    durable: true
);

// Sample product catalog by category for random generation
var categoryProducts = new Dictionary<string, string[]>
{
    { "A", ["Smartphone Galaxy", "Notebook Gamer", "Monitor 4K", "Smart TV 55\""] },
    { "B", ["Fone Bluetooth", "Teclado Mecânico", "Mouse Sem Fio", "Headset 7.1"] },
    { "C", ["Cadeira Ergonômica", "Mesa Articulada", "Suporte para Notebook", "Luminária LED"] }
};

while (true)
{
    Console.WriteLine("[MS.Promocoes] Menu: ");
    Console.WriteLine("1. Gerar e publicar promoção aleatória");
    Console.WriteLine("2. Publicar lote de promoções aleatórias");
    Console.WriteLine("3. Sair");
    Console.Write("Escolha uma opção: ");

    var option = Console.ReadLine()?.Trim();
    switch (option)
    {
        case "1":
            await PublishRandomPromotionAsync();
            break;

        case "2":
            await PublishBatchPromotionsAsync();
            break;

        case "3":
        case null:
            Console.WriteLine("[MS.Promocoes] Saindo...");
            return;

        default:
            Console.WriteLine("[MS.Promocoes] Opção inválida. Tente novamente.");
            break;
    }
}

async Task PublishRandomPromotionAsync()
{
    var promocao = GenerateRandomPromotion();
    await PublishPromotionAsync(promocao);
}

async Task PublishBatchPromotionsAsync()
{
    Console.Write("\nQuantidade de promoções aleatórias a publicar: ");
    if (!int.TryParse(Console.ReadLine(), out int count) || count <= 0)
    {
        Console.WriteLine("[MS.Promocoes] Quantidade inválida.");
        return;
    }

    Console.WriteLine($"[MS.Promocoes] Publicando {count} promoções...");
    for (int i = 1; i <= count; i++)
    {
        Console.WriteLine($"\n Promoção {i} de {count}");
        var promocao = GenerateRandomPromotion();
        await PublishPromotionAsync(promocao);
        if (i < count)
        {
            await Task.Delay(1000);
        }
    }
}

Promocao GenerateRandomPromotion()
{
    string[] categories = ["A", "B", "C"];
    string category = categories[Random.Shared.Next(categories.Length)];
    string[] products = categoryProducts[category];
    string product = products[Random.Shared.Next(products.Length)];
    decimal discount = Random.Shared.Next(2, 11) * 5; // 10%, 15%, ..., 50%

    return new Promocao
    {
        Categoria = category,
        Produto = product,
        DescontoPercentual = discount,
        Descricao = $"Imperdível: {discount}% OFF em {product} da categoria {category})!"
    };
}

async Task PublishPromotionAsync(Promocao promocao)
{
    string routingKey = $"promocao.categoria.{promocao.Categoria}";
    string contentJson = JsonSerializer.Serialize(promocao);
    string signature = signatureService.SignText(contentJson, privateKeyPath);

    var message = new Message<Promocao>
    {
        Producer = "MS.Promocoes",
        Content = promocao,
        Signature = signature
    };

    byte[] body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

    await channel.BasicPublishAsync(
        exchange: "Promoções",
        routingKey: routingKey,
        body: body
    );

    Console.WriteLine($"[MS.Promocoes] Evento publicado com sucesso!");
    Console.WriteLine($"  Exchange: Promoções (Topic)");
    Console.WriteLine($"  Routing Key: {routingKey}");
    Console.WriteLine($"  Produto: {promocao.Produto} | Categoria: {promocao.Categoria} | Desconto: {promocao.DescontoPercentual}%");
    Console.WriteLine($"  Descrição: {promocao.Descricao}");
}