using Catalogo.Data;
using Catalogo.Features.Products;
using Microsoft.EntityFrameworkCore;

namespace Catalogo.Features.Storefront;

/// <summary>
/// Recorte que o visitante pede: termo de busca, categoria, página. Tudo opcional — a
/// vitrine sem nenhum parâmetro é o acervo publicado inteiro, primeira página.
/// </summary>
public sealed record StorefrontRequest
{
    public const int DefaultPageSize = 12;

    public string? Term { get; init; }

    public int? CategoryId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;
}

/// <summary>Produto como a vitrine o desenha. A derivada de impressão nunca sai daqui
/// (RN-12) — a listagem usa a miniatura, o detalhe usa a ampliada.</summary>
public sealed record StorefrontProduct(
    int Id,
    string Name,
    string? Summary,
    decimal Price,
    PriceLabel PriceLabel,
    string CategoryName,
    string? ThumbnailFileName);

/// <summary>
/// Faceta de categoria: quantos produtos o visitante veria se escolhesse esta categoria,
/// dado o termo que ele já digitou (RN-50).
/// </summary>
public sealed record StorefrontCategory(int Id, string Name, int Products);

/// <summary>
/// Produto na página de detalhe. É o **único lugar do sistema** onde a descrição longa
/// aparece (RN-05, RN-53), e usa a derivada ampliada — não a de impressão, que nunca sai
/// para o público (RN-12).
/// </summary>
public sealed record StorefrontDetail(
    int Id,
    string Name,
    string? Summary,
    string? Description,
    decimal Price,
    PriceLabel PriceLabel,
    int CategoryId,
    string CategoryName,
    string? LargeFileName);

public sealed record StorefrontPage(
    IReadOnlyList<StorefrontProduct> Products,
    IReadOnlyList<StorefrontCategory> Categories,
    int Total,
    int Page,
    int PageSize)
{
    public int PageCount => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}

/// <summary>
/// Consulta pública da vitrine (RN-48 a RN-52). É a consulta mais executada do sistema e a
/// única que um anônimo alcança, então o filtro de situação não é conveniência de tela: é
/// a fronteira entre o que é público e o que não é, e vem do ponto único da RN-15.
///
/// A busca procura **exclusivamente no nome** (RN-49, ADR-004). Resumo e descrição não são
/// pesquisáveis — restrição declarada, não limitação a corrigir.
/// </summary>
public sealed class StorefrontQuery(IDbContextFactory<CatalogDbContext> contextFactory)
{
    private const int MaxPageSize = 60;

    /// <summary>
    /// A página e as facetas saem de duas consultas **disparadas juntas**, cada uma no seu
    /// contexto. O plano pedia "a mesma ida ao banco", e o motivo declarado era latência:
    /// a ADR-004, na revisão 0.7, registra que o banco passou a ser acessado pela rede e
    /// que o custo por consulta cresceu. Duas consultas concorrentes pagam uma latência,
    /// não duas, e continuam sendo LINQ tipado — a alternativa de uma instrução única
    /// exigiria SQL cru com função de janela, trocando verificabilidade por uma economia
    /// que o cache da T-21 vai absorver de todo modo.
    /// </summary>
    public async Task<StorefrontPage> SearchAsync(
        StorefrontRequest request,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);
        var term = Normalize(request.Term);

        var products = ProductsAsync(request.CategoryId, term, page, pageSize, cancellationToken);
        var categories = CategoriesAsync(term, cancellationToken);

        await Task.WhenAll(products, categories);

        var (items, total) = await products;

        return new StorefrontPage(items, await categories, total, page, pageSize);
    }

    /// <summary>
    /// Um produto publicado, ou <c>null</c>. O filtro da RN-15 vale aqui como na listagem,
    /// e é o que torna `UI-02.naoEncontrado` inevitável em vez de esquecível: despublicar
    /// um produto o faz deixar de existir para a vitrine, e links já compartilhados
    /// continuam sendo abertos.
    /// </summary>
    public async Task<StorefrontDetail?> FindAsync(
        int productId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Products
            .AsNoTracking()
            .Published()
            .Where(product => product.Id == productId)
            .Select(product => new StorefrontDetail(
                product.Id,
                product.Name,
                product.Summary,
                product.Description,
                product.Price,
                product.PriceLabel,
                product.CategoryId,
                product.Category!.Name,
                product.Photo == null ? null : product.Photo.LargeFileName))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Vizinhos da mesma categoria, na ordem curada, sem o próprio produto. O card do
    /// relacionado exibe **resumo**, nunca descrição (RN-04, ADR-016).
    /// </summary>
    public async Task<IReadOnlyList<StorefrontProduct>> RelatedAsync(
        StorefrontDetail product,
        int limit = 4,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Products
            .AsNoTracking()
            .Published()
            .Where(candidate =>
                candidate.CategoryId == product.CategoryId && candidate.Id != product.Id)
            .InCuratedOrder()
            .Take(limit)
            .Select(candidate => new StorefrontProduct(
                candidate.Id,
                candidate.Name,
                candidate.Summary,
                candidate.Price,
                candidate.PriceLabel,
                candidate.Category!.Name,
                candidate.Photo == null ? null : candidate.Photo.ThumbnailFileName))
            .ToListAsync(cancellationToken);
    }

    private async Task<(IReadOnlyList<StorefrontProduct> Items, int Total)> ProductsAsync(
        int? categoryId,
        string? term,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var matching = Searched(context.Products.AsNoTracking().Published(), term);

        if (categoryId is { } category)
        {
            matching = matching.Where(product => product.CategoryId == category);
        }

        var total = await matching.CountAsync(cancellationToken);

        var items = await matching
            .OrderBy(product => product.Category!.Position)
            .ThenBy(product => product.Category!.Name)
            .ThenInCuratedOrder()
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(product => new StorefrontProduct(
                product.Id,
                product.Name,
                product.Summary,
                product.Price,
                product.PriceLabel,
                product.Category!.Name,
                product.Photo == null ? null : product.Photo.ThumbnailFileName))
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    /// <summary>
    /// A faceta respeita o termo e **ignora a categoria escolhida**: ela responde "quantos
    /// eu veria se escolhesse esta categoria". Aplicar o filtro de categoria aqui zeraria
    /// todas as outras e o filtro deixaria de ser navegável (RN-50).
    ///
    /// Categoria sem nenhum produto publicado no recorte não aparece — não há para onde o
    /// visitante ir nela.
    /// </summary>
    private async Task<IReadOnlyList<StorefrontCategory>> CategoriesAsync(
        string? term,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await Searched(context.Products.AsNoTracking().Published(), term)
            .GroupBy(product => new
            {
                product.CategoryId,
                product.Category!.Name,
                product.Category.Position
            })
            .OrderBy(group => group.Key.Position)
            .ThenBy(group => group.Key.Name)
            .Select(group => new StorefrontCategory(
                group.Key.CategoryId,
                group.Key.Name,
                group.Count()))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Cada palavra do termo precisa aparecer no nome, em qualquer ordem. Uma só
    /// expressão com `%` entre as palavras exigiria a ordem digitada, e "mãe placa" não
    /// acharia "Placa-Mãe" — o que a busca de uma vitrine não pode fazer.
    ///
    /// A comparação normaliza o acento nos **dois lados**, com a mesma função imutável
    /// sobre a qual o índice de expressão foi construído (ADR-004, CA-22).
    /// </summary>
    private static IQueryable<Product> Searched(IQueryable<Product> products, string? term)
    {
        if (term is null)
        {
            return products;
        }

        foreach (var word in term.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var pattern = $"%{Escape(word)}%";

            products = products.Where(product => EF.Functions.ILike(
                CatalogDbContext.Unaccent(product.Name),
                CatalogDbContext.Unaccent(pattern),
                @"\"));
        }

        return products;
    }

    private static string? Normalize(string? term) =>
        string.IsNullOrWhiteSpace(term) ? null : term.Trim();

    /// <summary>
    /// O termo vem do visitante, então os curingas do `like` chegam como texto. Sem isto,
    /// um `%` solto devolveria o acervo inteiro e um `_` casaria qualquer caractere.
    /// </summary>
    private static string Escape(string word) => word
        .Replace(@"\", @"\\")
        .Replace("%", @"\%")
        .Replace("_", @"\_");
}
