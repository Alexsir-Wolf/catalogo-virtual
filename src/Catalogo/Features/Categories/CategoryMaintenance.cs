using Catalogo.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Catalogo.Features.Categories;

public enum MoveDirection
{
    Up,
    Down
}

public enum CategoryFailure
{
    None,
    NameRequired,
    NameAlreadyInUse
}

public sealed record CategoryOutcome(CategoryFailure Failure)
{
    public bool Succeeded => Failure == CategoryFailure.None;

    public static readonly CategoryOutcome Success = new(CategoryFailure.None);
}

/// <summary>
/// Manutenção das categorias. A unicidade do nome (RN-23) é decidida pelo índice do
/// banco, não por uma consulta prévia: duas abas abertas ao mesmo tempo contornariam a
/// verificação em memória, e o índice não tem como ser contornado.
/// </summary>
public sealed class CategoryMaintenance(IDbContextFactory<CatalogDbContext> contextFactory)
{
    public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Categories
            .AsNoTracking()
            .OrderBy(category => category.Position)
            .ThenBy(category => category.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<CategoryOutcome> CreateAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return new CategoryOutcome(CategoryFailure.NameRequired);
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var lastPosition = await context.Categories
            .Select(category => (int?)category.Position)
            .MaxAsync(cancellationToken) ?? 0;

        context.Categories.Add(new Category { Name = trimmed, Position = lastPosition + 1 });

        return await SaveAsync(context, cancellationToken);
    }

    public async Task<CategoryOutcome> RenameAsync(
        int id,
        string name,
        CancellationToken cancellationToken = default)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return new CategoryOutcome(CategoryFailure.NameRequired);
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var category = await context.Categories
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (category is null)
        {
            return CategoryOutcome.Success;
        }

        category.Name = trimmed;

        return await SaveAsync(context, cancellationToken);
    }

    /// <summary>
    /// Troca a categoria de lugar com a vizinha na direção pedida. A posição é inteiro
    /// sequencial e a troca renumera apenas as duas envolvidas — com a ordem de grandeza
    /// de categorias deste catálogo, renumerar é mais simples do que manter lacunas
    /// (ADR-015). Mover a primeira para cima ou a última para baixo não faz nada.
    /// </summary>
    public async Task MoveAsync(
        int id,
        MoveDirection direction,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var ordered = await context.Categories
            .OrderBy(category => category.Position)
            .ThenBy(category => category.Name)
            .ToListAsync(cancellationToken);

        var index = ordered.FindIndex(category => category.Id == id);
        if (index < 0)
        {
            return;
        }

        var target = direction == MoveDirection.Up ? index - 1 : index + 1;
        if (target < 0 || target >= ordered.Count)
        {
            return;
        }

        // A posição gravada pode ter lacunas ou empates herdados; normalizar a lista
        // inteira deixa a numeração exibida e a ordem persistida sempre coerentes.
        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);

        for (var position = 0; position < ordered.Count; position++)
        {
            ordered[position].Position = position + 1;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task<CategoryOutcome> SaveAsync(
        CatalogDbContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return CategoryOutcome.Success;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return new CategoryOutcome(CategoryFailure.NameAlreadyInUse);
        }
    }
}
