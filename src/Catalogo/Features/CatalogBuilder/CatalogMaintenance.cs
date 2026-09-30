using Catalogo.Data;
using Catalogo.Features.Products;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Catalogo.Features.CatalogBuilder;

public enum CatalogFailure
{
    None,
    NameRequired,
    NameAlreadyInUse,
    NoCategorySelected,
    NotFound,

    /// <summary>
    /// Alguma categoria marcada não existe mais: outra aba a excluiu entre o carregamento da tela
    /// e o salvamento. É recusa **própria**, e não <see cref="NoCategorySelected"/>, porque o dono
    /// marcou categorias — o que sumiu foi a categoria, e o próximo passo dele é recarregar a
    /// lista e refazer o recorte, não "marcar ao menos uma".
    /// </summary>
    CategoryNoLongerExists,

    /// <summary>
    /// O banco recusou a gravação do critério e a reconsulta não encontrou categoria faltando — o
    /// caso restante é o conflito na chave da tabela de junção, com duas abas salvando o mesmo
    /// catálogo ao mesmo tempo.
    ///
    /// Não vira <see cref="NameAlreadyInUse"/>, que mandaria o dono procurar um problema de nome
    /// inexistente, nem <see cref="CategoryNoLongerExists"/>, que afirmaria uma exclusão que a
    /// reconsulta não viu. O que resta de verdadeiro é que outra aba mexeu neste catálogo, e o
    /// próximo passo é recarregar e reler o critério gravado.
    /// </summary>
    ConcurrentChange
}

public sealed record CatalogOutcome(CatalogFailure Failure, int? Id = null)
{
    public bool Succeeded => Failure == CatalogFailure.None;

    public static CatalogOutcome Saved(int id) => new(CatalogFailure.None, id);
}

/// <summary>
/// O critério de um catálogo, como a tela o entrega.
/// </summary>
public sealed record CatalogDraft
{
    public int? Id { get; init; }

    public string Name { get; set; } = string.Empty;

    public List<int> CategoryIds { get; set; } = [];
}

/// <summary>
/// Um catálogo com o que a lista precisa exibir: o critério salvo, a última geração e a
/// contagem **resolvida agora**.
/// </summary>
public sealed record CatalogSummary(
    int Id,
    string Name,
    IReadOnlyList<string> CategoryNames,
    int PublishedProducts,
    DateTimeOffset? LastGeneratedAt)
{
    public bool WasGenerated => LastGeneratedAt is not null;
}

/// <summary>
/// Manutenção dos catálogos salvos (RN-26 a RN-29, RN-33, RN-34).
///
/// **O que este serviço não faz é o ponto:** ele nunca persiste a lista de produtos de um
/// catálogo (RN-29, ADR-014). A contagem que a lista exibe é resolvida por consulta no
/// momento da exibição (RN-30) e pode mudar entre duas visitas sem ninguém ter mexido em
/// nada — é comportamento correto, e a tela diz que o número é de agora.
///
/// A unicidade do nome é decidida pelo índice do banco, e não por uma consulta prévia: duas
/// abas abertas ao mesmo tempo contornariam a verificação em memória, e o índice não. É a
/// mesma decisão da RN-23 nas categorias.
/// </summary>
public sealed class CatalogMaintenance(
    IDbContextFactory<CatalogDbContext> contextFactory,
    TimeProvider time)
{
    /// <summary>
    /// Os catálogos com a contagem de produtos **No ar** que cada critério resolve agora.
    /// Produto em Rascunho não conta: ele não sai no documento (RN-15, RN-30).
    /// </summary>
    public async Task<IReadOnlyList<CatalogSummary>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Catalogs
            .AsNoTracking()
            .OrderBy(catalog => catalog.Name)
            .Select(catalog => new CatalogSummary(
                catalog.Id,
                catalog.Name,
                catalog.Categories
                    .OrderBy(link => link.Category.Position)
                    .Select(link => link.Category.Name)
                    .ToList(),
                context.Products.Count(product =>
                    product.Status == ProductStatus.Published
                    && catalog.Categories.Any(link => link.CategoryId == product.CategoryId)),
                catalog.LastGeneratedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<CatalogDraft?> FindAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Catalogs
            .AsNoTracking()
            .Where(catalog => catalog.Id == id)
            .Select(catalog => new CatalogDraft
            {
                Id = catalog.Id,
                Name = catalog.Name,
                CategoryIds = catalog.Categories.Select(link => link.CategoryId).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Cria ou atualiza o critério. Recusa nome vazio (RN-27) e critério sem categoria
    /// (RN-28) — abranger o acervo inteiro é selecionar todas, não nenhuma.
    /// </summary>
    public async Task<CatalogOutcome> SaveAsync(
        CatalogDraft draft,
        CancellationToken cancellationToken = default,
        bool retrying = false)
    {
        var name = draft.Name?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            return new CatalogOutcome(CatalogFailure.NameRequired);
        }

        var categoryIds = draft.CategoryIds.Distinct().ToList();

        if (categoryIds.Count == 0)
        {
            return new CatalogOutcome(CatalogFailure.NoCategorySelected);
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var catalog = draft.Id is { } id
            ? await context.Catalogs
                .Include(candidate => candidate.Categories)
                .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            : new Catalog();

        if (catalog is null)
        {
            return new CatalogOutcome(CatalogFailure.NotFound);
        }

        catalog.Name = name;

        // O critério é substituído, não mesclado: a tela entrega a seleção inteira, e
        // acumular as anteriores faria o catálogo crescer sozinho a cada salvamento.
        //
        // **A data de entrada de quem já estava é preservada.** Regravar tudo com a hora de agora
        // apagaria a informação que a RN-32 usa: uma categoria que está no critério desde março
        // não passou a integrar o catálogo neste salvamento, e destacar os produtos dela seria
        // alarme falso.
        var now = time.GetUtcNow();
        var previousEntry = catalog.Categories
            .ToDictionary(link => link.CategoryId, link => link.AddedAt);

        catalog.Categories.Clear();
        catalog.Categories.AddRange(categoryIds.Select(categoryId => new CatalogCategory
        {
            CategoryId = categoryId,
            AddedAt = previousEntry.TryGetValue(categoryId, out var previous) ? previous : now
        }));

        if (draft.Id is null)
        {
            context.Catalogs.Add(catalog);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: NameIndex
            })
        {
            // Qualificado **pela restrição**, e não por qualquer violação de unicidade: sem isso,
            // um conflito na chave da tabela de junção — possível com duas abas salvando o mesmo
            // catálogo — viraria a mensagem "já existe um catálogo com este nome", que manda o
            // dono procurar um problema de nome que não existe.
            return new CatalogOutcome(CatalogFailure.NameAlreadyInUse);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.UniqueViolation
            })
        {
            // O que sobra depois do filtro acima são as duas corridas entre abas: a categoria
            // marcada foi excluída em outra aba — violação de chave estrangeira na junção — ou
            // duas abas gravaram o mesmo catálogo ao mesmo tempo, e a segunda bateu na chave da
            // junção. Sem este braço, as duas saíam como exceção crua, e exceção crua vinda de um
            // manipulador de evento do Blazor **derruba o circuito**: o dono perde a tela e o
            // critério que acabou de digitar. É o mesmo sintoma que o review de T-16 corrigiu nos
            // produtos.
            //
            // A recusa é **reconsultada em vez de inventada**, como em `CategoryMaintenance`: só se
            // afirma que uma categoria sumiu depois de perguntar ao banco quais das marcadas ainda
            // existem. Dizer "categoria excluída" sem conferir rotularia de exclusão o conflito de
            // duas abas, que tem outro próximo passo.
            var surviving = await context.Categories
                .CountAsync(category => categoryIds.Contains(category.Id), cancellationToken);

            if (surviving < categoryIds.Count)
            {
                return new CatalogOutcome(CatalogFailure.CategoryNoLongerExists);
            }

            // **Uma tentativa só.** Se a corrida se resolveu entre o erro e a reconsulta, refazer
            // grava e o dono não vê nada; se não, ele recebe a recusa e decide. Pior que isso seria
            // repetir indefinidamente dentro de uma requisição.
            return retrying
                ? new CatalogOutcome(CatalogFailure.ConcurrentChange)
                : await SaveAsync(draft, cancellationToken, retrying: true);
        }

        return CatalogOutcome.Saved(catalog.Id);
    }

    /// <summary>
    /// Remove o filtro salvo. **Nenhum produto é afetado** (RN-34, CA-20): o catálogo nunca
    /// foi dono de produto algum, e é por isso que excluí-lo é barato e sem consequência.
    /// </summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var catalog = await context.Catalogs
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (catalog is null)
        {
            return false;
        }

        context.Catalogs.Remove(catalog);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>Marca a geração (RN-33). Quem gera o documento é T-25; o registro é aqui.</summary>
    public async Task MarkGeneratedAsync(
        int id,
        DateTimeOffset moment,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await context.Catalogs
            .Where(catalog => catalog.Id == id)
            .ExecuteUpdateAsync(
                update => update.SetProperty(catalog => catalog.LastGeneratedAt, moment),
                cancellationToken);
    }

    /// <summary>O índice que garante a RN-27. O nome vem da convenção do EF para `HasIndex`.</summary>
    private const string NameIndex = "IX_Catalogs_Name";
}
