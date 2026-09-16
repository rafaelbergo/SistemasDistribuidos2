namespace Foundation.Models;

public class ConsultaProdutos
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
}

public class CatalogoProdutos
{
    public string ConsultaId { get; set; } = string.Empty;
    public List<ProdutoDisponivel> Produtos { get; set; } = [];
}

public class ProdutoDisponivel
{
    public string Id { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public int QuantidadeDisponivel { get; set; }
}
