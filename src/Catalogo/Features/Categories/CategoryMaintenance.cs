using Catalogo.Data;
using Catalogo.Features.Storefront;
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
    NameAlreadyInUse,
    HasProducts,

    /// <summary>
    /// Retida por integrar catálogo salvo (RN-25.1). É **distinta** de <see cref="HasProducts"/>
    /// de propósito: a causa e a saída são diferentes — num caso o dono move ou exclui produtos,
    /// no outro ele edita o critério de um catálogo. Unificar em "categoria em uso" deixaria a
    /// pessoa sem saber o que fazer.
    /// </summary>
    UsedByCatalogs,

    /// <summary>
    /// O banco recusou a exclusão e a reconsulta não conseguiu explicar por quê — o impedimento
    /// apareceu, sumiu e reapareceu entre a verificação e o commit, em outra aba.
    ///
    /// É causa **própria**, e não <see cref="UsedByCatalogs"/> com payload vazio: rotular toda
    /// violação de chave estrangeira como catálogo mentiria sobre a causa quando quem segura é um
    /// produto, e uma recusa sem nome nenhum não tem como ser desenhada pelos blocos que dependem
    /// de payload — chegaria à tela como **silêncio**, que numa tela de exclusão é a pior resposta
    /// possível porque convida ao segundo clique. Sem poder nomear quem bloqueia, o próximo passo
    /// que a RN-25.1 exige passa a ser outro: recarregar a lista e ler o estado novo.
    /// </summary>
    ConcurrentChange
}

public sealed record CategoryOutcome(CategoryFailure Failure)
{
    public bool Succeeded => Failure == CategoryFailure.None;

    public static readonly CategoryOutcome Success = new(CategoryFailure.None);
}

/// <summary>
/// Recusa de exclusão, com o número que a mensagem precisa informar (RN-25). T-26
/// acrescenta aqui a segunda condição — catálogo que usa a categoria (RN-25.1) —, e por
/// isso a verificação vive em um ponto só.
/// </summary>
public sealed record CategoryDeletionOutcome(
    CategoryFailure Failure,
    int BlockingProducts,
    IReadOnlyList<string>? BlockingCatalogs = null)
{
    public bool Succeeded => Failure == CategoryFailure.None;

    /// <summary>Os catálogos que retêm a categoria, nomeados (RN-25.1).</summary>
    public IReadOnlyList<string> Catalogs => BlockingCatalogs ?? [];

    public static readonly CategoryDeletionOutcome Success = new(CategoryFailure.None, 0);
}

/// <summary>
/// Manutenção das categorias. A unicidade do nome (RN-23) é decidida pelo índice do
/// banco, não por uma consulta prévia: duas abas abertas ao mesmo tempo contornariam a
/// verificação em memória, e o índice não tem como ser contornado.
/// </summary>
public sealed class CategoryMaintenance(
    IDbContextFactory<CatalogDbContext> contextFactory,
    StorefrontInvalidation cache)
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

        // A ordem das categorias é a ordem do filtro da vitrine (RN-24).
        await cache.InvalidateAsync("ordem de categoria alterada", cancellationToken);
    }

    /// <summary>
    /// Exclui a categoria, salvo se houver produto associado — em qualquer situação,
    /// Rascunho inclusive, porque um rascunho perderia a categoria sem aviso (RN-25).
    /// </summary>
    public async Task<CategoryDeletionOutcome> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default,
        bool retrying = false)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var blockingProducts = await context.Products
            .CountAsync(product => product.CategoryId == id, cancellationToken);

        // RN-25.1: a categoria também fica retida enquanto integrar algum catálogo salvo, ainda
        // que esteja **vazia**. Sem isso, excluir uma categoria sem produtos deixaria um
        // catálogo sem critério resolvível — o "catálogo órfão" que a lacuna 8 da SPEC-UI
        // levantou. O `Restrict` da FK impediria a exclusão no banco, mas como exceção crua;
        // aqui a recusa vira mensagem que **nomeia** os catálogos.
        var blockingCatalogs = await context.Catalogs
            .Where(catalog => catalog.Categories.Any(link => link.CategoryId == id))
            .OrderBy(catalog => catalog.Name)
            .Select(catalog => catalog.Name)
            .ToListAsync(cancellationToken);

        // **As duas causas voltam juntas**, e não a primeira que aparecer.
        //
        // Antes, produtos tinham precedência e a recusa por catálogo só aparecia depois: o dono de
        // uma categoria com vinte produtos que também integra um catálogo movia os vinte, voltava, e
        // **só então** descobria o segundo impedimento. A tela já sabe mostrar os dois parágrafos;
        // era a consulta que parava na primeira. O `Failure` continua sendo o de produtos quando
        // há produtos, porque é a causa que o dono resolve primeiro de qualquer jeito.
        if (blockingProducts > 0 || blockingCatalogs.Count > 0)
        {
            return new CategoryDeletionOutcome(
                blockingProducts > 0 ? CategoryFailure.HasProducts : CategoryFailure.UsedByCatalogs,
                blockingProducts,
                blockingCatalogs);
        }

        var category = await context.Categories
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (category is null)
        {
            return CategoryDeletionOutcome.Success;
        }

        context.Categories.Remove(category);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.ForeignKeyViolation
            })
        {
            // A verificação acima e este commit não são atômicos: entre um e outro, outra aba pode
            // ter cadastrado um produto na categoria ou acrescentado ela ao critério de um catálogo.
            // O `Restrict` da FK impede a exclusão no banco, mas como exceção crua — e exceção crua
            // saindo de um manipulador de evento do Blazor **derruba o circuito** do painel, que é o
            // mesmo sintoma que o review de T-16 corrigiu.
            //
            // A recusa é reconsultada em vez de inventada: dizer "categoria em uso" sem nomear
            // quem a usa deixaria o dono sem o próximo passo, que é o que a RN-25.1 exige.
            //
            // **Uma tentativa só.** Se a corrida se resolver entre o erro e a reconsulta, o dono
            // recebe a recusa e clica de novo — pior que isso seria repetir indefinidamente dentro
            // de uma requisição.
            //
            // Esgotada a retentativa, a recusa é <see cref="CategoryFailure.ConcurrentChange"/> e
            // não uma recusa por catálogo sem catálogo nenhum: quem consome decide pela causa, e
            // uma recusa de payload vazio rotulada de catálogo não era desenhável nem verdadeira.
            return retrying
                ? new CategoryDeletionOutcome(CategoryFailure.ConcurrentChange, BlockingProducts: 0)
                : await DeleteAsync(id, cancellationToken, retrying: true);
        }

        await cache.InvalidateAsync("categoria excluída", cancellationToken);

        return CategoryDeletionOutcome.Success;
    }

    private async Task<CategoryOutcome> SaveAsync(
        CatalogDbContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);

            // O nome da categoria e a ordem dela aparecem na vitrine, no filtro e no título da
            // listagem: criar e renomear mudam a página pública.
            await cache.InvalidateAsync("categoria criada ou renomeada", cancellationToken);

            return CategoryOutcome.Success;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return new CategoryOutcome(CategoryFailure.NameAlreadyInUse);
        }
    }
}
