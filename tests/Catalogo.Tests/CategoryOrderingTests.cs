using Catalogo.Data;
using Catalogo.Features.Categories;
using Microsoft.EntityFrameworkCore;

namespace Catalogo.Tests;

[Collection(PostgresCollection.Name)]
public sealed class CategoryOrderingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Mover_para_cima_persiste_a_nova_ordem()
    {
        var maintenance = CreateMaintenance();
        var (first, second) = await CreatePairAsync(maintenance);

        await maintenance.MoveAsync(second.Id, MoveDirection.Up);

        var ordered = await OrderedIdsAsync(maintenance, first.Id, second.Id);

        Assert.Equal([second.Id, first.Id], ordered);
    }

    [Fact]
    public async Task Mover_para_baixo_persiste_a_nova_ordem()
    {
        var maintenance = CreateMaintenance();
        var (first, second) = await CreatePairAsync(maintenance);

        await maintenance.MoveAsync(first.Id, MoveDirection.Down);

        var ordered = await OrderedIdsAsync(maintenance, first.Id, second.Id);

        Assert.Equal([second.Id, first.Id], ordered);
    }

    [Fact]
    public async Task Primeira_categoria_nao_sobe()
    {
        var maintenance = CreateMaintenance();
        var all = await maintenance.ListAsync();
        var head = all.Count > 0 ? all[0] : await CreateAsync(maintenance, "Primeira");

        await maintenance.MoveAsync(head.Id, MoveDirection.Up);

        var reloaded = await maintenance.ListAsync();

        Assert.Equal(head.Id, reloaded[0].Id);
    }

    [Fact]
    public async Task Ultima_categoria_nao_desce()
    {
        var maintenance = CreateMaintenance();
        await CreateAsync(maintenance, "Ultima");

        var tail = (await maintenance.ListAsync())[^1];

        await maintenance.MoveAsync(tail.Id, MoveDirection.Down);

        var reloaded = await maintenance.ListAsync();

        Assert.Equal(tail.Id, reloaded[^1].Id);
    }

    [Fact]
    public async Task Posicoes_ficam_sequenciais_apos_o_movimento()
    {
        var maintenance = CreateMaintenance();
        var (_, second) = await CreatePairAsync(maintenance);

        await maintenance.MoveAsync(second.Id, MoveDirection.Up);

        var positions = (await maintenance.ListAsync())
            .Select(category => category.Position)
            .ToList();

        Assert.Equal(Enumerable.Range(1, positions.Count), positions);
    }

    private async Task<(Category First, Category Second)> CreatePairAsync(CategoryMaintenance maintenance)
    {
        var first = await CreateAsync(maintenance, "Anterior");
        var second = await CreateAsync(maintenance, "Posterior");

        return (first, second);
    }

    private static async Task<Category> CreateAsync(CategoryMaintenance maintenance, string prefix)
    {
        var name = $"{prefix} {Guid.NewGuid():N}";
        await maintenance.CreateAsync(name);

        return (await maintenance.ListAsync()).Single(category => category.Name == name);
    }

    private static async Task<List<int>> OrderedIdsAsync(
        CategoryMaintenance maintenance,
        params int[] ids) =>
        (await maintenance.ListAsync())
            .Where(category => ids.Contains(category.Id))
            .Select(category => category.Id)
            .ToList();

    private CategoryMaintenance CreateMaintenance() =>
        new(new ContextFactory(postgres.ConnectionString));

    private sealed class ContextFactory(string connectionString) : IDbContextFactory<CatalogDbContext>
    {
        public CatalogDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connectionString).Options);
    }
}
