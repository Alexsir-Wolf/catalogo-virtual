using Catalogo.Data;
using Catalogo.Features.Categories;
using Microsoft.EntityFrameworkCore;

namespace Catalogo.Tests;

[Collection(PostgresCollection.Name)]
public sealed class CategoryMaintenanceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Categoria_com_nome_valido_persiste_e_aparece_na_lista()
    {
        var maintenance = CreateMaintenance();
        var name = UniqueName("Impressoras");

        var outcome = await maintenance.CreateAsync(name);

        Assert.True(outcome.Succeeded);
        Assert.Contains(await maintenance.ListAsync(), category => category.Name == name);
    }

    [Fact]
    public async Task RN_23_nome_duplicado_e_recusado()
    {
        var maintenance = CreateMaintenance();
        var name = UniqueName("Tintas");

        await maintenance.CreateAsync(name);
        var outcome = await maintenance.CreateAsync(name);

        Assert.Equal(CategoryFailure.NameAlreadyInUse, outcome.Failure);
    }

    [Fact]
    public async Task Nome_vazio_e_recusado()
    {
        var maintenance = CreateMaintenance();

        var outcome = await maintenance.CreateAsync("   ");

        Assert.Equal(CategoryFailure.NameRequired, outcome.Failure);
    }

    [Fact]
    public async Task Espacos_ao_redor_do_nome_nao_criam_duplicata_disfarcada()
    {
        var maintenance = CreateMaintenance();
        var name = UniqueName("Cabos");

        await maintenance.CreateAsync(name);
        var outcome = await maintenance.CreateAsync($"  {name}  ");

        Assert.Equal(CategoryFailure.NameAlreadyInUse, outcome.Failure);
    }

    /// <summary>
    /// A categoria organiza o PDF e o filtro da vitrine. Duas que o dono lê como a mesma
    /// virariam duas seções numeradas no impresso — por isso "único" da RN-23 não pode
    /// distinguir maiúsculas.
    /// </summary>
    [Fact]
    public async Task RN_23_nome_que_difere_apenas_na_caixa_e_recusado()
    {
        var maintenance = CreateMaintenance();
        var name = UniqueName("Tintas");

        await maintenance.CreateAsync(name);
        var outcome = await maintenance.CreateAsync(name.ToUpperInvariant());

        Assert.Equal(CategoryFailure.NameAlreadyInUse, outcome.Failure);
    }

    [Fact]
    public async Task Renomear_para_nome_que_difere_apenas_na_caixa_e_recusado()
    {
        var maintenance = CreateMaintenance();
        var existing = UniqueName("Cimento");
        var other = UniqueName("Argamassa");

        await maintenance.CreateAsync(existing);
        await maintenance.CreateAsync(other);

        var target = (await maintenance.ListAsync()).Single(category => category.Name == other);
        var outcome = await maintenance.RenameAsync(target.Id, existing.ToUpperInvariant());

        Assert.Equal(CategoryFailure.NameAlreadyInUse, outcome.Failure);
    }

    [Fact]
    public async Task Renomear_para_nome_ja_existente_e_recusado()
    {
        var maintenance = CreateMaintenance();
        var existing = UniqueName("Papelaria");
        var renamed = UniqueName("Escritorio");

        await maintenance.CreateAsync(existing);
        await maintenance.CreateAsync(renamed);

        var target = (await maintenance.ListAsync()).Single(category => category.Name == renamed);
        var outcome = await maintenance.RenameAsync(target.Id, existing);

        Assert.Equal(CategoryFailure.NameAlreadyInUse, outcome.Failure);
    }

    [Fact]
    public async Task Renomear_com_nome_valido_persiste()
    {
        var maintenance = CreateMaintenance();
        var original = UniqueName("Monitores");
        var updated = UniqueName("Telas");

        await maintenance.CreateAsync(original);
        var target = (await maintenance.ListAsync()).Single(category => category.Name == original);

        var outcome = await maintenance.RenameAsync(target.Id, updated);

        Assert.True(outcome.Succeeded);
        Assert.Contains(await maintenance.ListAsync(), category => category.Name == updated);
    }

    private CategoryMaintenance CreateMaintenance() =>
        new(new PooledContextFactory(postgres.ConnectionString));

    private static string UniqueName(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    private sealed class PooledContextFactory(string connectionString)
        : IDbContextFactory<CatalogDbContext>
    {
        public CatalogDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<CatalogDbContext>()
                .UseNpgsql(connectionString)
                .Options);
    }
}
