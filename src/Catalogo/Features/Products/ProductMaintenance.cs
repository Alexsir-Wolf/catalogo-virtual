using Catalogo.Data;
using Catalogo.Features.Categories;
using Microsoft.EntityFrameworkCore;

namespace Catalogo.Features.Products;

public sealed record ProductDraft
{
    public int? Id { get; init; }

    public string Name { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public string? Description { get; set; }

    public decimal? Price { get; set; }

    public PriceLabel PriceLabel { get; set; } = PriceLabel.Price;

    public int? CategoryId { get; set; }
}

public enum ProductField
{
    Name,
    Summary,
    Price,
    Category
}

public sealed record ProductOutcome(int? Id, IReadOnlyDictionary<ProductField, string> Errors)
{
    public bool Succeeded => Errors.Count == 0;

    public static ProductOutcome Saved(int id) =>
        new(id, new Dictionary<ProductField, string>());
}

/// <summary>
/// Cadastro e edição de produto. Os três campos de texto têm destinos distintos e nenhum
/// deriva do outro (ADR-016); a validação do resumo existe para proteger a grade do PDF,
/// não por capricho de formulário.
/// </summary>
public sealed class ProductMaintenance(IDbContextFactory<CatalogDbContext> contextFactory)
{
    public async Task<IReadOnlyList<Category>> ListCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Categories
            .AsNoTracking()
            .OrderBy(category => category.Position)
            .ThenBy(category => category.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<ProductDraft?> FindAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var product = await context.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (product is null)
        {
            return null;
        }

        return new ProductDraft
        {
            Id = product.Id,
            Name = product.Name,
            Summary = product.Summary,
            Description = product.Description,
            Price = product.Price,
            PriceLabel = product.PriceLabel,
            CategoryId = product.CategoryId
        };
    }

    public async Task<ProductOutcome> SaveAsync(
        ProductDraft draft,
        CancellationToken cancellationToken = default)
    {
        var errors = Validate(draft);
        if (errors.Count > 0)
        {
            return new ProductOutcome(draft.Id, errors);
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var product = draft.Id is { } id
            ? await context.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            : null;

        if (product is null)
        {
            product = new Product { Name = draft.Name.Trim() };

            // Todo produto nasce em Rascunho (RN-14), e a posição vai para o fim da
            // categoria — reposicionar é ação separada, de T-15.
            product.Status = ProductStatus.Draft;
            product.Position = await NextPositionAsync(context, draft.CategoryId!.Value, cancellationToken);

            context.Products.Add(product);
        }

        product.Name = draft.Name.Trim();
        product.Summary = Blank(draft.Summary);
        product.Description = Blank(draft.Description);
        product.Price = draft.Price!.Value;
        product.PriceLabel = draft.PriceLabel;
        product.CategoryId = draft.CategoryId!.Value;

        await context.SaveChangesAsync(cancellationToken);

        return ProductOutcome.Saved(product.Id);
    }

    private static Dictionary<ProductField, string> Validate(ProductDraft draft)
    {
        var errors = new Dictionary<ProductField, string>();

        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            errors[ProductField.Name] = "Informe o nome do produto.";
        }

        // O resumo é opcional, mas o limite protege a célula do PDF (RN-03, ADR-016).
        if (draft.Summary?.Trim().Length > Product.SummaryMaxLength)
        {
            errors[ProductField.Summary] =
                $"O resumo passa de {Product.SummaryMaxLength} caracteres e não caberia na célula do PDF.";
        }

        if (draft.Price is null or <= 0)
        {
            errors[ProductField.Price] = "O preço é obrigatório e precisa ser maior que zero.";
        }

        if (draft.CategoryId is null)
        {
            errors[ProductField.Category] = "Escolha a categoria do produto.";
        }

        return errors;
    }

    private static async Task<int> NextPositionAsync(
        CatalogDbContext context,
        int categoryId,
        CancellationToken cancellationToken)
    {
        var last = await context.Products
            .Where(product => product.CategoryId == categoryId)
            .Select(product => (int?)product.Position)
            .MaxAsync(cancellationToken);

        return (last ?? 0) + 1;
    }

    /// <summary>Texto em branco é ausência, não string vazia — a célula do PDF e o card
    /// decidem o que exibir pela ausência do resumo (RN-04).</summary>
    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
