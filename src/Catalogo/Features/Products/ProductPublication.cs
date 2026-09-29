using Catalogo.Data;
using Microsoft.EntityFrameworkCore;

namespace Catalogo.Features.Products;

/// <summary>
/// Campo que a RN-16 exige preenchido para o produto passar a No ar. A lista é fechada e
/// não inclui resumo nem descrição — a RN-16 nomeia quatro campos, e exigir um quinto é
/// justamente o erro que o plano manda evitar.
/// </summary>
public enum PublicationRequirement
{
    Name,
    Price,
    Category,
    Photo
}

/// <summary>
/// Resultado de uma transição de situação. <see cref="Missing"/> vazia significa que a
/// transição aconteceu; preenchida, que a publicação foi recusada e nada mudou (RN-17).
/// </summary>
public sealed record PublicationOutcome(
    ProductStatus Status,
    IReadOnlyList<PublicationRequirement> Missing)
{
    public bool Succeeded => Missing.Count == 0;

    public static PublicationOutcome Published() => new(ProductStatus.Published, []);

    public static PublicationOutcome Withdrawn() => new(ProductStatus.Draft, []);

    public static PublicationOutcome Refused(IReadOnlyList<PublicationRequirement> missing) =>
        new(ProductStatus.Draft, missing);
}

/// <summary>
/// Transição entre Rascunho e No ar (RN-14, RN-18). A pré-condição da RN-16 é conferida
/// aqui, contra o que está gravado, e não na tela: o botão desabilitado é conveniência,
/// não garantia — a tela pode estar desatualizada em relação ao banco.
///
/// Publicar não toca em catálogo nenhum. Os produtos de um catálogo são resolvidos no
/// momento da geração, considerando apenas os que estão No ar (RN-19, RN-30, ADR-014),
/// de modo que a inserção "em todos os catálogos que a categoria atende" é consequência
/// da situação gravada, não de uma escrita adicional.
/// </summary>
public sealed class ProductPublication(
    IDbContextFactory<CatalogDbContext> contextFactory,
    TimeProvider time,
    ILogger<ProductPublication> logger)
{
    /// <returns><c>null</c> quando o produto não existe mais — quem chama mostra isso em
    /// vez de tratar como recusa de publicação.</returns>
    public async Task<PublicationOutcome?> PublishAsync(
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

        var missing = MissingRequirements(product);
        if (missing.Count > 0)
        {
            logger.LogInformation(
                "Publicação do produto {ProductId} recusada. Falta: {Missing}.",
                productId,
                string.Join(", ", missing));

            return PublicationOutcome.Refused(missing);
        }

        product.Status = ProductStatus.Published;

        // A data de entrada no ar é o que permite à prévia destacar o que é novo desde a
        // última geração (RN-32), sem guardar a lista de produtos de catálogo nenhum.
        product.PublishedAt = time.GetUtcNow();

        await context.SaveChangesAsync(cancellationToken);

        return PublicationOutcome.Published();
    }

    /// <summary>
    /// Volta o produto a Rascunho (RN-18). Não há pré-condição: tirar do ar é sempre
    /// possível, e o efeito é imediato porque toda consulta pública filtra pela situação.
    /// </summary>
    public async Task<PublicationOutcome?> WithdrawAsync(
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

        product.Status = ProductStatus.Draft;
        await context.SaveChangesAsync(cancellationToken);

        return PublicationOutcome.Withdrawn();
    }

    /// <summary>
    /// Situação gravada e o que ainda falta para publicar, para a tela nomear a pendência
    /// antes de o dono tentar (UI-05.publicacaoBloqueada).
    /// </summary>
    public async Task<PublicationState?> FindStateAsync(
        int productId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var product = await context.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == productId, cancellationToken);

        return product is null
            ? null
            : new PublicationState(product.Status, MissingRequirements(product));
    }

    /// <summary>
    /// Pré-condição da RN-16 avaliada sobre o que está gravado. Resumo e descrição não
    /// entram: a regra nomeia nome, preço, categoria e foto, e só (RN-16).
    /// </summary>
    private static IReadOnlyList<PublicationRequirement> MissingRequirements(Product product)
    {
        var missing = new List<PublicationRequirement>();

        if (string.IsNullOrWhiteSpace(product.Name))
        {
            missing.Add(PublicationRequirement.Name);
        }

        if (product.Price <= 0)
        {
            missing.Add(PublicationRequirement.Price);
        }

        if (product.CategoryId == 0)
        {
            missing.Add(PublicationRequirement.Category);
        }

        if (product.Photo is null)
        {
            missing.Add(PublicationRequirement.Photo);
        }

        return missing;
    }
}

/// <summary>Situação gravada do produto e a distância que falta para publicá-lo.</summary>
public sealed record PublicationState(
    ProductStatus Status,
    IReadOnlyList<PublicationRequirement> Missing)
{
    public bool CanPublish => Missing.Count == 0;
}
