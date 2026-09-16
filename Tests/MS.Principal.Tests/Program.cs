using Foundation;
using Foundation.Models;
using MS.Principal;

string root = Path.Combine(Path.GetTempPath(), "principal-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed = 0;

Run("CRUD persists drafts, edits and logical deletion across restarts", () =>
{
    var (repo, path) = Store();
    var pedido = Order();
    repo.Criar(pedido);
    repo = new PedidoRepository(path);
    Equal("rascunho", repo.Consultar(pedido.Id)!.Status);
    repo.Editar(pedido.Id, "cliente-editado", [new ItemPedido { Id = "B", Quantidade = 4 }]);
    repo = new PedidoRepository(path);
    Equal("cliente-editado", repo.Consultar(pedido.Id)!.Pedido.ClienteId);
    Equal(4, repo.Consultar(pedido.Id)!.Pedido.Itens.Single().Quantidade);
    repo.Excluir(pedido.Id);
    repo = new PedidoRepository(path);
    Equal(0, repo.Listar().Count);
    Equal(true, repo.Listar(true).Single().Excluido);
    Equal(3, repo.Listar(true).Single().Historico.Count);
});

Run("Submission persists the order and a stable pending message together", () =>
{
    var (repo, path) = Store();
    var pedido = Order();
    repo.Criar(pedido);
    repo.Enviar(pedido.Id);
    var pending = repo.PublicacoesPendentes().Single();
    repo = new PedidoRepository(path);
    Equal("pedido.criado", repo.Consultar(pedido.Id)!.Status);
    Equal(pending.MessageId, repo.PublicacoesPendentes().Single().MessageId);
    Equal(pedido.Id, repo.PublicacoesPendentes().Single().Content.Id);
    Throws<InvalidOperationException>(() => repo.Enviar(pedido.Id));
    Throws<InvalidOperationException>(() => repo.Editar(pedido.Id, "outro", pedido.Itens));
    Throws<InvalidOperationException>(() => repo.Excluir(pedido.Id));
    repo.ConfirmarPublicacao(pending.MessageId);
    Equal(0, new PedidoRepository(path).PublicacoesPendentes().Count);
});

Run("All lifecycle statuses and history persist, without overwriting order data", () =>
{
    foreach (var terminal in new[] { "pagamento.recusado", "estoque.indisponivel", "pedido.enviado" })
    {
        var (repo, path) = Store();
        var pedido = Order();
        repo.Criar(pedido); repo.Enviar(pedido.Id);
        var payload = Order(); payload.Id = pedido.Id; payload.ClienteId = "conteudo-antigo";
        string[] events = terminal == "estoque.indisponivel" ? [terminal]
            : terminal == "pagamento.recusado" ? ["pedido.estoque_ok", terminal]
            : ["pedido.estoque_ok", "pagamento.aprovado", terminal];
        foreach (string evento in events)
        {
            repo.AplicarEvento(evento, Event(payload));
            repo = new PedidoRepository(path);
            Equal(evento, repo.Consultar(pedido.Id)!.Status);
        }
        Equal(pedido.ClienteId, repo.Consultar(pedido.Id)!.Pedido.ClienteId);
        Equal(events.Length + 2, repo.Consultar(pedido.Id)!.Historico.Count);
        repo.Excluir(pedido.Id);
        repo.AplicarEvento("pedido.enviado", Event(pedido));
        Equal(0, new PedidoRepository(path).Listar().Count);
    }
});

Run("Duplicate events remain idempotent after restart", () =>
{
    var (repo, path) = Store(); var pedido = Order();
    repo.Criar(pedido); repo.Enviar(pedido.Id);
    var message = Event(pedido);
    repo.AplicarEvento("pagamento.recusado", message);
    repo = new PedidoRepository(path);
    int count = repo.Consultar(pedido.Id)!.Historico.Count;
    repo.AplicarEvento("pagamento.recusado", message);
    Equal(count, repo.Consultar(pedido.Id)!.Historico.Count);
});

Run("Late and conflicting events cannot regress or resurrect terminal orders", () =>
{
    foreach (string status in new[] { "pedido.enviado", "pagamento.recusado", "estoque.indisponivel" })
    {
        var (repo, _) = Store(); var pedido = Order();
        repo.Criar(pedido); repo.Enviar(pedido.Id);
        repo.AplicarEvento(status, Event(pedido));
        foreach (string evento in new[] { "pedido.estoque_ok", "pagamento.aprovado", "pedido.enviado", "pagamento.recusado" })
            repo.AplicarEvento(evento, Event(pedido));
        var saved = repo.Consultar(pedido.Id)!;
        Equal(status, saved.Status);
        Equal(4, saved.Historico.Count(h => !h.Aplicado));
    }
});

Run("Events do not create unknown orders or submit drafts", () =>
{
    var (repo, _) = Store(); var pedido = Order();
    repo.AplicarEvento("pedido.enviado", Event(pedido));
    Equal(0, repo.Listar().Count);
    repo.Criar(pedido);
    repo.AplicarEvento("pedido.enviado", Event(pedido));
    Equal("rascunho", repo.Consultar(pedido.Id)!.Status);
});

Run("Validation rejects empty orders, duplicates and nonpositive quantities", () =>
{
    var (repo, _) = Store();
    foreach (var pedido in new[]
    {
        new PedidoCriado { ClienteId = "cliente" },
        new PedidoCriado { ClienteId = "cliente", Itens = [new ItemPedido { Id = "A", Quantidade = 0 }] },
        new PedidoCriado { ClienteId = "cliente", Itens = [new ItemPedido { Id = "A" }, new ItemPedido { Id = "A" }] },
        new PedidoCriado { Itens = [new ItemPedido { Id = "A" }] }
    }) Throws<InvalidOperationException>(() => repo.Criar(pedido));
    Equal(0, repo.Listar().Count);
});

Run("Snapshots cannot mutate saved state", () =>
{
    var (repo, _) = Store(); var pedido = Order(); repo.Criar(pedido);
    pedido.Itens[0].Quantidade = 500;
    repo.Listar()[0].Pedido.ClienteId = "externo";
    Equal(2, repo.Consultar(pedido.Id)!.Pedido.Itens[0].Quantidade);
    Equal("cliente", repo.Consultar(pedido.Id)!.Pedido.ClienteId);
});

Run("Concurrent menu changes and events are not lost", () =>
{
    var (repo, path) = Store(); var pedido = Order(); repo.Criar(pedido); repo.Enviar(pedido.Id);
    Parallel.Invoke(
        () => { for (int i = 0; i < 15; i++) repo.Criar(Order()); },
        () => { for (int i = 0; i < 15; i++) repo.AplicarEvento("pedido.estoque_ok", Event(pedido)); });
    var reloaded = new PedidoRepository(path);
    Equal(16, reloaded.Listar().Count);
    Equal(17, reloaded.Consultar(pedido.Id)!.Historico.Count);
});

Run("Failed disk writes leave both memory and saved state unchanged", () =>
{
    var (repo, path) = Store(); var pedido = Order(); repo.Criar(pedido);
    Directory.CreateDirectory(path + ".tmp");
    try { repo.Enviar(pedido.Id); throw new Exception("Expected a filesystem failure"); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    Equal("rascunho", repo.Consultar(pedido.Id)!.Status);
    Equal(0, repo.PublicacoesPendentes().Count);
    Equal("rascunho", new PedidoRepository(path).Consultar(pedido.Id)!.Status);
});

Run("Corrupt storage fails startup and is preserved", () =>
{
    string path = Path.Combine(root, Guid.NewGuid() + ".json");
    File.WriteAllText(path, "invalid JSON");
    Throws<System.Text.Json.JsonException>(() => new PedidoRepository(path));
    Equal("invalid JSON", File.ReadAllText(path));
});

Console.WriteLine($"PASS: {passed} tests. Temporary data: {root}");

void Run(string name, Action test) { test(); passed++; Console.WriteLine("PASS: " + name); }
(PedidoRepository, string) Store()
{
    string path = Path.Combine(root, Guid.NewGuid() + ".json");
    return (new PedidoRepository(path), path);
}
static PedidoCriado Order() => new() { ClienteId = "cliente", Itens = [new ItemPedido { Id = "A", Quantidade = 2 }] };
static Message<PedidoCriado> Event(PedidoCriado pedido) => new() { Producer = "test", Content = pedido };
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}");
}
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
