using Foundation;
using Foundation.Models;
using System.Text.Json;

namespace MS.Principal;

public sealed class PedidoRegistro
{
    public PedidoCriado Pedido { get; set; } = new();
    public string Status { get; set; } = "rascunho";
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
    public bool Excluido { get; set; }
    public List<PedidoHistorico> Historico { get; set; } = [];
}

public sealed class PedidoHistorico
{
    public string Evento { get; set; } = "";
    public string Produtor { get; set; } = "MS.Principal";
    public string? MensagemId { get; set; }
    public DateTime DataEvento { get; set; } = DateTime.UtcNow;
    public DateTime RecebidoEm { get; set; } = DateTime.UtcNow;
    public bool Aplicado { get; set; } = true;
}

public sealed class PedidoDatabase
{
    public List<PedidoRegistro> Pedidos { get; set; } = [];
    public List<Message<PedidoCriado>> PublicacoesPendentes { get; set; } = [];
}

// One Principal process owns the file. Its menu, consumer and publisher share this lock.
public sealed class PedidoRepository
{
    private readonly object gate = new();
    private readonly string path;
    private PedidoDatabase database;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public PedidoRepository(string path)
    {
        this.path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);
        // An unreadable/corrupt file must fail startup instead of erasing saved orders.
        database = File.Exists(this.path)
            ? JsonSerializer.Deserialize<PedidoDatabase>(File.ReadAllText(this.path))
                ?? throw new InvalidDataException("Arquivo de pedidos inválido.")
            : new PedidoDatabase();
        if (database.Pedidos is null || database.PublicacoesPendentes is null)
            throw new InvalidDataException("Estrutura do arquivo de pedidos inválida.");
    }

    public List<PedidoRegistro> Listar(bool incluirExcluidos = false)
    {
        lock (gate)
            return Copy(database.Pedidos.Where(p => incluirExcluidos || !p.Excluido)
                .OrderByDescending(p => p.CriadoEm).ToList());
    }

    public PedidoRegistro? Consultar(string id)
    {
        lock (gate)
            return Copy(database.Pedidos.Find(p => p.Pedido.Id == id && !p.Excluido));
    }

    public PedidoRegistro Criar(PedidoCriado pedido, bool enviar = false)
    {
        Validar(pedido);
        return Alterar(db =>
        {
            if (db.Pedidos.Any(p => p.Pedido.Id == pedido.Id))
                throw new InvalidOperationException("ID de pedido já cadastrado.");
            var registro = new PedidoRegistro { Pedido = Copy(pedido), Status = enviar ? "pedido.criado" : "rascunho" };
            registro.Historico.Add(new PedidoHistorico { Evento = enviar ? "pedido.criado" : "rascunho.criado" });
            db.Pedidos.Add(registro);
            if (enviar)
                db.PublicacoesPendentes.Add(new Message<PedidoCriado> { Producer = "MS.Principal", Content = Copy(pedido) });
            return registro;
        });
    }

    public void Editar(string id, string clienteId, List<ItemPedido> itens)
    {
        var pedido = new PedidoCriado { Id = id, ClienteId = clienteId, Itens = itens };
        Validar(pedido);
        Alterar(db =>
        {
            var registro = Encontrar(db, id);
            if (registro.Status != "rascunho")
                throw new InvalidOperationException("Somente rascunhos podem ser editados. O pedido já foi enviado ao processamento.");
            registro.Pedido = Copy(pedido);
            RegistrarAcao(registro, "rascunho.editado");
            return true;
        });
    }

    public void Enviar(string id)
    {
        Alterar(db =>
        {
            var registro = Encontrar(db, id);
            if (registro.Status != "rascunho")
                throw new InvalidOperationException("Este pedido já foi enviado ao processamento.");
            registro.Status = "pedido.criado";
            RegistrarAcao(registro, "pedido.criado");
            // The order and the event are saved together before any network operation.
            db.PublicacoesPendentes.Add(new Message<PedidoCriado>
            {
                Producer = "MS.Principal",
                Content = Copy(registro.Pedido)
            });
            return true;
        });
    }

    public void Excluir(string id)
    {
        Alterar(db =>
        {
            var registro = Encontrar(db, id);
            if (registro.Status is not ("rascunho" or "pagamento.recusado" or "estoque.indisponivel" or "pedido.enviado"))
                throw new InvalidOperationException("Aguarde o processamento terminar antes de excluir este pedido.");
            registro.Excluido = true;
            RegistrarAcao(registro, "registro.excluido");
            return true;
        });
    }

    public string AplicarEvento(string evento, Message<PedidoCriado> mensagem)
    {
        return Alterar(db =>
        {
            var registro = db.Pedidos.Find(p => p.Pedido.Id == mensagem.Content.Id);
            if (registro is null) return "pedido não cadastrado; evento ignorado";
            if (registro.Excluido) return "pedido excluído; evento ignorado";
            if (registro.Historico.Any(h => h.MensagemId == mensagem.MessageId))
                return "evento duplicado; ignorado";

            bool aplicado = PodeAvancar(registro.Status, evento);
            registro.Historico.Add(new PedidoHistorico
            {
                Evento = evento,
                Produtor = mensagem.Producer,
                MensagemId = mensagem.MessageId,
                DataEvento = mensagem.Timestamp,
                Aplicado = aplicado
            });
            if (aplicado) registro.Status = evento;
            registro.AtualizadoEm = DateTime.UtcNow;
            // Payloads of delayed events never overwrite the locally saved order.
            return aplicado ? registro.Status : $"{registro.Status} (evento atrasado/incompatível registrado no histórico)";
        });
    }

    public List<Message<PedidoCriado>> PublicacoesPendentes()
    {
        lock (gate) return Copy(database.PublicacoesPendentes);
    }

    public void ConfirmarPublicacao(string mensagemId) => Alterar(db =>
        db.PublicacoesPendentes.RemoveAll(m => m.MessageId == mensagemId));

    private T Alterar<T>(Func<PedidoDatabase, T> alterar)
    {
        lock (gate)
        {
            var proxima = Copy(database);
            T resultado = alterar(proxima);
            string temporario = path + ".tmp";
            using (var stream = new FileStream(temporario, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, proxima, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporario, path, overwrite: true);
            database = proxima;
            return Copy(resultado);
        }
    }

    private static PedidoRegistro Encontrar(PedidoDatabase db, string id) =>
        db.Pedidos.Find(p => p.Pedido.Id == id && !p.Excluido)
        ?? throw new InvalidOperationException("Pedido não encontrado.");

    private static void RegistrarAcao(PedidoRegistro registro, string evento)
    {
        registro.AtualizadoEm = DateTime.UtcNow;
        registro.Historico.Add(new PedidoHistorico { Evento = evento });
    }

    private static bool PodeAvancar(string atual, string evento) => atual switch
    {
        "pedido.criado" => evento is "pedido.estoque_ok" or "estoque.indisponivel"
            or "pagamento.aprovado" or "pagamento.recusado" or "pedido.enviado",
        "pedido.estoque_ok" => evento is "pagamento.aprovado" or "pagamento.recusado" or "pedido.enviado",
        "pagamento.aprovado" => evento == "pedido.enviado",
        _ => false
    };

    private static void Validar(PedidoCriado pedido)
    {
        if (string.IsNullOrWhiteSpace(pedido.Id) || string.IsNullOrWhiteSpace(pedido.ClienteId))
            throw new InvalidOperationException("Informe o ID do cliente e do pedido.");
        if (pedido.Itens is null || pedido.Itens.Count == 0 ||
            pedido.Itens.Any(i => i is null || string.IsNullOrWhiteSpace(i.Id) || i.Quantidade <= 0) ||
            pedido.Itens.Select(i => i.Id).Distinct().Count() != pedido.Itens.Count)
            throw new InvalidOperationException("Informe itens únicos, com ID e quantidade maior que zero.");
    }

    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
