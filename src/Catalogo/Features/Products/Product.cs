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
    /// Fixado em T-04 (RN-03). O texto de produto do gabarito está em 11,2 pt, o que
    /// acomoda ~34 caracteres por linha na coluna de ~51 mm; em quatro linhas o bloco
    /// comporta ~136 caracteres, dos quais o nome consome ~40. Os 160 do spike de T-03
    /// pressupunham 7,5 pt — letra menor que a do catálogo do cliente.
    /// </summary>
    public const int SummaryMaxLength = 120;

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
