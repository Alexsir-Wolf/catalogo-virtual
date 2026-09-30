using Catalogo.Data;
using Catalogo.Features.Products;
using Microsoft.EntityFrameworkCore;

namespace Catalogo.Features.CatalogBuilder;

/// <summary>Um produto na prévia, já com a numeração que vai sair no papel.</summary>
public sealed record ResolvedProduct(
    int Id,
    string Name,
    string? Summary,
    decimal Price,
    PriceLabel PriceLabel,
    string? ThumbnailFileName,

    /// <summary>
    /// A derivada **de impressão** (RN-11, RN-12). A prévia usa a miniatura; o documento usa
    /// esta. Consumir a de tela no PDF produz página borrada, e o erro só aparece no papel —
    /// é o risco que o plano de T-24 destaca.
    /// </summary>
    string? PrintFileName,

    bool IsNewSinceLastGeneration);

/// <summary>
/// Uma categoria do recorte, com o número **posicional dentro deste catálogo** (RN-39).
/// </summary>
public sealed record ResolvedCategory(
    int Id,
    string Name,
    int Number,
    IReadOnlyList<ResolvedProduct> Products)
{
    /// <summary>`01`, `02` — como sai impresso.</summary>
    public string Label => Number.ToString("D2");
}

/// <summary>
/// O que a prévia mostra e o que a geração vai compor.
/// </summary>
public sealed record ResolvedCatalog(
    int CatalogId,
    string Name,
    IReadOnlyList<ResolvedCategory> Categories,
    DateTimeOffset? LastGeneratedAt)
{
    public int ProductCount => Categories.Sum(category => category.Products.Count);

    public int NewSinceLastGeneration =>
        Categories.Sum(category => category.Products.Count(product => product.IsNewSinceLastGeneration));

    public bool HasNewProducts => NewSinceLastGeneration > 0;

    /// <summary>
    /// Vazio quando o critério não resolve **nenhum** produto No ar (RN-46) — inclui o caso de
    /// todos estarem em Rascunho, que é o que o CA-18 descreve.
    /// </summary>
    public bool IsEmpty => ProductCount == 0;

    /// <summary>
    /// Estimativa de páginas de conteúdo. Vem da medição do spike de T-03: 36 produtos na
    /// grade de três colunas ocuparam 4 páginas, e o título de cada categoria mais a última
    /// linha incompleta já estão diluídos nessa média.
    ///
    /// É **estimativa**, e a tela precisa dizer isso: a contagem exata só existe depois de
    /// compor, porque uma célula que não cabe desce inteira para a página seguinte (RN-42).
    /// </summary>
    public int EstimatedPages => Math.Max(1, (int)Math.Ceiling(ProductCount / (double)ProductsPerPage));

    public const int ProductsPerPage = 9;
}

/// <summary>
/// Resolve o critério salvo em uma lista concreta de produtos (RN-30), no momento em que
/// alguém pede — nunca antes, nunca guardado (ADR-014).
///
/// Três regras moram aqui e cada uma tem uma forma errada e tentadora de implementar:
///
/// **RN-39, a numeração.** O número da categoria é **posicional dentro deste catálogo**: um
/// recorte com a 3ª e a 6ª categorias globais as imprime como `01` e `02`. Usar o
/// identificador ou a posição global produziria `03` e `06`, e é o erro que o plano aponta
/// como o mais fácil de cometer.
///
/// **RN-40, a ordem.** As categorias saem na ordem **global** do cadastro, e os produtos na
/// posição dentro da categoria. Ou seja: a ordem é a global, a numeração é a local — as duas
/// coisas ao mesmo tempo, e é isso que confunde.
///
/// **RN-32, o destaque.** Produto que passou a integrar o catálogo desde a última geração é
/// marcado. Isso não é enfeite: é a mitigação do risco central do sistema — o dono gera um
/// catálogo achando que conhece o conteúdo e descobre no cliente que algo entrou.
/// </summary>
public sealed class CatalogResolution(IDbContextFactory<CatalogDbContext> contextFactory)
{
    public async Task<ResolvedCatalog?> ResolveAsync(
        int catalogId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var catalog = await context.Catalogs
            .AsNoTracking()
            .Where(candidate => candidate.Id == catalogId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Name,
                candidate.LastGeneratedAt,
                Criterion = candidate.Categories
                    .Select(link => new { link.CategoryId, link.AddedAt })
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (catalog is null)
        {
            return null;
        }

        var categoryIds = catalog.Criterion.Select(link => link.CategoryId).ToList();
        var criterionEntry = catalog.Criterion.ToDictionary(
            link => link.CategoryId,
            link => link.AddedAt);

        // Só produtos No ar (RN-30, RN-15). Rascunho não sai no documento, e é o que faz o
        // CA-18 existir: um catálogo cujas categorias só têm rascunho resolve vazio.
        var products = await context.Products
            .AsNoTracking()
            .Where(product =>
                product.Status == ProductStatus.Published
                && categoryIds.Contains(product.CategoryId))
            .OrderBy(product => product.Category!.Position)
            .ThenBy(product => product.Position)
            .ThenBy(product => product.Id)
            .Select(product => new
            {
                product.Id,
                product.Name,
                product.Summary,
                product.Price,
                product.PriceLabel,
                product.CategoryId,
                CategoryName = product.Category!.Name,
                CategoryPosition = product.Category.Position,
                Thumbnail = product.Photo!.ThumbnailFileName,
                Print = product.Photo.PrintFileName,
                product.PublishedAt,
                product.CategorizedAt
            })
            .ToListAsync(cancellationToken);

        var categories = products
            .GroupBy(product => new { product.CategoryId, product.CategoryName, product.CategoryPosition })
            .OrderBy(group => group.Key.CategoryPosition)
            .ThenBy(group => group.Key.CategoryName)
            .Select((group, index) => new ResolvedCategory(
                group.Key.CategoryId,
                group.Key.CategoryName,

                // Aqui está a RN-39: o número vem da **ordem no recorte**, não do cadastro.
                Number: index + 1,

                group.Select(product => new ResolvedProduct(
                    product.Id,
                    product.Name,
                    product.Summary,
                    product.Price,
                    product.PriceLabel,
                    product.Thumbnail,
                    product.Print,
                    IsNew(
                        product.PublishedAt,
                        product.CategorizedAt,
                        criterionEntry.GetValueOrDefault(product.CategoryId),
                        catalog.LastGeneratedAt))).ToList()))
            .ToList();

        return new ResolvedCatalog(catalog.Id, catalog.Name, categories, catalog.LastGeneratedAt);
    }

    /// <summary>
    /// Novo desde a última geração (RN-32). Catálogo nunca gerado **não destaca nada**: se
    /// tudo é novo, destacar tudo não informa nada, e o dono nunca gerou este recorte — ele
    /// não tem expectativa anterior a contrariar.
    ///
    /// Há **três** caminhos para um produto passar a integrar o catálogo, e a RN-32 fala do
    /// resultado, não do caminho. Cada um deles foi descoberto custando um review:
    ///
    /// 1. O produto foi **publicado** depois da última geração. É o caminho óbvio, e o único que a
    ///    primeira versão cobria.
    /// 2. A **categoria entrou no critério** depois dela. Acrescentar uma categoria com trinta
    ///    produtos publicados em julho traz os trinta de uma vez, e comparar só a publicação não
    ///    destacaria nenhum.
    /// 3. O produto **mudou de categoria** para dentro do critério depois dela. Aqui a publicação é
    ///    antiga **e** a categoria está no critério desde sempre: os dois primeiros caminhos são
    ///    cegos, e o produto entra no PDF entregue sem aviso nenhum.
    ///
    /// Os três levam ao mesmo lugar, que é o risco central do sistema: o dono gera um catálogo
    /// achando que conhece o conteúdo e descobre no cliente que algo entrou.
    /// </summary>
    private static bool IsNew(
        DateTimeOffset? publishedAt,
        DateTimeOffset categorizedAt,
        DateTimeOffset categoryAddedAt,
        DateTimeOffset? lastGeneratedAt)
    {
        if (lastGeneratedAt is not { } generated)
        {
            return false;
        }

        return categoryAddedAt > generated
            || categorizedAt > generated
            || (publishedAt is { } published && published > generated);
    }
}
