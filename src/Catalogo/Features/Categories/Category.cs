namespace Catalogo.Features.Categories;

/// <summary>
/// Agrupamento de produtos. O nome é único (RN-23) e a posição define tanto a ordem
/// na vitrine quanto a numeração impressa no PDF (RN-24, ADR-015).
/// </summary>
public class Category
{
    public const int NameMaxLength = 60;

    public int Id { get; set; }

    public required string Name { get; set; }

    public int Position { get; set; }
}
