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
public sealed class ProductMaintenance(
    IDbContextFactory<CatalogDbContext> contextFactory,
    ILogger<ProductMaintenance> logger)
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
            // Todo produto nasce em Rascunho (RN-14). A posição é atribuída adiante, pelo
            // mesmo caminho que trata a troca de categoria — entrar numa categoria é
            // sempre entrar no fim dela, seja no cadastro ou na edição.
            product = new Product { Name = draft.Name.Trim(), Status = ProductStatus.Draft };

            context.Products.Add(product);
        }

        product.Name = draft.Name.Trim();
        product.Summary = Blank(draft.Summary);
        product.Description = Blank(draft.Description);
        product.Price = draft.Price!.Value;
        product.PriceLabel = draft.PriceLabel;

        // Trocar de categoria é entrar numa fila nova, e a posição antiga não vale nela:
        // mantida, o produto cai no meio da categoria de destino empatado com quem já
        // ocupa aquele número, e a ordem impressa sai de duas formas diferentes (ADR-015).
        // Vai para o fim, que é o que a criação faz — reposicionar é ação separada, de T-15.
        if (product.CategoryId != draft.CategoryId!.Value)
        {
            product.CategoryId = draft.CategoryId.Value;
            product.Position = await NextPositionAsync(context, product.CategoryId, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);

        return ProductOutcome.Saved(product.Id);
    }

    /// <summary>
    /// Associa ao produto as derivadas recém-geradas. O produto tem uma foto só, então
    /// isto substitui a referência anterior em vez de acrescentar (RN-09).
    ///
    /// As derivadas antigas **não são apagadas do armazenamento**: uma página da vitrine
    /// já servida do cache ainda aponta para elas, e removê-las na hora quebraria a
    /// imagem até a invalidação. No volume previsto o acúmulo é pequeno; os nomes
    /// substituídos vão para o log para não ficarem sem rastro.
    /// </summary>
    public async Task<ProductPhoto?> AttachPhotoAsync(
        int productId,
        ProductPhoto photo,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var product = await context.Products
            .SingleOrDefaultAsync(candidate => candidate.Id == productId, cancellationToken);

        if (product is null)
        {
            return null;
        }

        var replaced = product.Photo;
        product.Photo = photo;

        await context.SaveChangesAsync(cancellationToken);

        if (replaced is not null)
        {
            logger.LogInformation(
                "Foto do produto {ProductId} substituída. Derivadas sem referência: {Names}.",
                productId,
                string.Join(", ", ReplacedNames(replaced)));
        }

        return photo;
    }

    public async Task<ProductPhoto?> FindPhotoAsync(
        int productId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Products
            .AsNoTracking()
            .Where(product => product.Id == productId)
            .Select(product => product.Photo)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Exclusão definitiva (RN-20, CA-08). Devolve a foto que existia, para quem chamou
    /// mandar remover os arquivos — o serviço de mídia é que conhece os buckets.
    ///
    /// **O registro sai primeiro, os arquivos depois.** A ordem importa: o banco é a fonte de
    /// verdade do acervo, e o que o dono pediu foi que o produto deixasse de existir. Se a
    /// remoção dos arquivos falhar, sobram objetos órfãos — desperdício, registrado em log.
    /// Na ordem inversa, uma falha de banco deixaria um produto no acervo **sem imagem
    /// alguma**, que é pior: a vitrine passaria a exibir um item quebrado.
    ///
    /// Produto inexistente devolve nulo em vez de lançar: excluir duas vezes na mesma aba é
    /// acidente comum, e a segunda vez não é erro — o resultado pedido já é o estado atual.
    /// </summary>
    public async Task<ProductPhoto?> DeleteAsync(
        int productId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var product = await context.Products
            .SingleOrDefaultAsync(candidate => candidate.Id == productId, cancellationToken);

        if (product is null)
        {
            return null;
        }

        var photo = product.Photo;

        context.Products.Remove(product);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Outra aba chegou primeiro: o DELETE não afetou linha nenhuma. O estado pedido já
            // é o estado atual, então isto é sucesso e não falha — dizer "não foi possível
            // excluir" para um produto que **foi** excluído deixaria o dono num formulário de
            // algo que não existe mais.
            logger.LogInformation(
                "Produto {Id} já havia sido excluído por outro fluxo.",
                productId);

            return null;
        }

        logger.LogInformation(
            "Produto {Id} excluído definitivamente. Tinha foto: {TinhaFoto}.",
            productId,
            photo is not null);

        return photo;
    }

    private static IEnumerable<string> ReplacedNames(ProductPhoto photo) =>
    [
        photo.OriginalFileName,
        photo.ThumbnailFileName,
        photo.CardFileName,
        photo.LargeFileName,
        photo.PrintFileName
    ];

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
