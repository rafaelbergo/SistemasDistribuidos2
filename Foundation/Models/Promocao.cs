namespace Foundation.Models;

public class Promocao
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Categoria { get; set; } = string.Empty;
    public string Produto { get; set; } = string.Empty;
    public decimal DescontoPercentual { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public DateTime Data { get; set; } = DateTime.UtcNow;
}