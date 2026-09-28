using Catalogo.Data;
using Microsoft.EntityFrameworkCore;

namespace Catalogo.Features.Products;

/// <summary>Recorte por situação que o painel oferece (UI-04).</summary>
public enum ProductSituationFilter
{
    All,
    Published,
    Drafts
}

/// <summary>
/// Categoria como faixa da listagem. O <see cref="Number"/> é posicional sobre a ordem
/// global das categorias, e não um identificador armazenado (ADR-015).
/// </summary>
public sealed record ProductListGroup(
    int Number,
    string CategoryName,
    IReadOnlyList<ProductListItem> Products);

/// <summary>
/// Linha da listagem. Traz só o que a linha desenha — a tela de edição é quem carrega o
/// produto inteiro. <see cref="ThumbnailFileName"/> nulo é ausência de foto, que a UI-04
/// exibe como marcador de falta e não como espaço vazio.
/// </summary>
public sealed record ProductListItem(
    int Id,
    string Name,
    string? Summary,
    decimal Price,
    PriceLabel PriceLabel,
    ProductStatus Status,
    string? ThumbnailFileName);

/// <summary>
/// Listagem do acervo no painel (RN-51). É a única tela onde o produto em Rascunho
/// aparece (RN-15) — a vitrine e os catálogos só veem o que está No ar.
///
/// A ordem é a global: categorias pela posição delas, produtos pela posição dentro da
/// categoria (RN-22, RN-24, ADR-015). A mesma ordem sai no PDF, então o que o dono vê
/// aqui é o que vai ser impresso.
/// </summary>
public sealed class ProductListing(IDbContextFactory<CatalogDbContext> contextFactory)
{
    /// <summary>
    /// Devolve o acervo inteiro, **sem paginar**. A tarefa T-17 ofereceu paginar ou
    /// carregar imagem sob demanda; entregou-se a segunda, e a primeira foi descartada
    /// porque a ADR-013 fixa teto de 250 produtos e o agrupamento por categoria precisa
    /// da lista completa para numerar as faixas e decidir os extremos de cada grupo.
    ///
    /// O número que torna esta decisão falsa é o teto: acima de ~250 produtos, ou se a
    /// ADR-013 for revista, a página passa a transmitir linhas demais — e o peso do HTML
    /// não é o que <c>loading="lazy"</c> resolve, porque cada linha vive também no
    /// `RenderTree` do circuito. Medir em T-29, quando o acervo real entrar.
    /// </summary>
    public async Task<IReadOnlyList<ProductListGroup>> ListAsync(
        ProductSituationFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var products = await Filtered(context.Products.AsNoTracking(), filter)
            .OrderBy(product => product.Category!.Position)
            .ThenBy(product => product.Category!.Name)
            .ThenInCuratedOrder()
            .Select(product => new
            {
                product.CategoryId,
                CategoryName = product.Category!.Name,
                CategoryPosition = product.Category.Position,
                Item = new ProductListItem(
                    product.Id,
                    product.Name,
                    product.Summary,
                    product.Price,
                    product.PriceLabel,
                    product.Status,
                    product.Photo == null ? null : product.Photo.ThumbnailFileName)
            })
            .ToListAsync(cancellationToken);

        // A numeração precisa da ordem global das categorias, e não da ordem das que
        // sobraram no recorte: filtrar por rascunhos não pode renumerar o acervo, senão o
        // número deixaria de ser uma propriedade da categoria e passaria a ser da visão.
        var numbers = await NumbersByCategoryAsync(context, cancellationToken);

        return products
            .GroupBy(row => new { row.CategoryId, row.CategoryName, row.CategoryPosition })
            .Select(group => new ProductListGroup(
                numbers[group.Key.CategoryId],
                group.Key.CategoryName,
                [.. group.Select(row => row.Item)]))
            .ToList();
    }

    private static IQueryable<Product> Filtered(
        IQueryable<Product> products,
        ProductSituationFilter filter) => filter switch
        {
            ProductSituationFilter.Published => products.Published(),
            ProductSituationFilter.Drafts => products.Drafts(),
            _ => products
        };

    private static async Task<IReadOnlyDictionary<int, int>> NumbersByCategoryAsync(
        CatalogDbContext context,
        CancellationToken cancellationToken)
    {
        var ordered = await context.Categories
            .AsNoTracking()
            .OrderBy(category => category.Position)
            .ThenBy(category => category.Name)
            .Select(category => category.Id)
            .ToListAsync(cancellationToken);

        return ordered
            .Select((categoryId, index) => (categoryId, Number: index + 1))
            .ToDictionary(entry => entry.categoryId, entry => entry.Number);
    }
}
