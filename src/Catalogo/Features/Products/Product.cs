using Catalogo.Features.Categories;

namespace Catalogo.Features.Products;

/// <summary>
/// Item do acervo. Os três campos de texto têm destinos distintos e nenhum deriva do
/// outro (ADR-016): o nome aparece em todo lugar, o resumo alimenta a célula do PDF e o
/// card da listagem, e a descrição existe apenas na página de detalhe.
/// </summary>
public class Product
{
    public const int NameMaxLength = 120;

    /// <summary>
    /// Medido no spike de T-03 como o que cabe em quatro linhas da célula do gabarito.
    /// Continua provisório até T-04 fixar o número contra o papel impresso (RN-03).
    /// </summary>
    public const int SummaryMaxLength = 160;

    public int Id { get; set; }

    public required string Name { get; set; }

    public string? Summary { get; set; }

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public PriceLabel PriceLabel { get; set; } = PriceLabel.Price;

    public int CategoryId { get; set; }

    public Category? Category { get; set; }

    /// <summary>
    /// Posição dentro da categoria, definida manualmente pelo dono. Vale ao mesmo tempo
    /// para a ordem no PDF e para a ordenação padrão da vitrine (RN-21, RN-22, ADR-015).
    /// </summary>
    public int Position { get; set; }

    public ProductStatus Status { get; set; } = ProductStatus.Draft;

    /// <summary>
    /// Nomes imutáveis das derivadas da foto. Substituir a foto gera nomes novos em vez
    /// de sobrescrever os anteriores (RN-11, RN-13, ADR-005). A derivada de impressão
    /// nunca é exposta publicamente (RN-12).
    /// </summary>
    public ProductPhoto? Photo { get; set; }
}
