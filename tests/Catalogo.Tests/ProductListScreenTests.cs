using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Catalogo.Data;
using Catalogo.Features.Account;
using Catalogo.Features.Categories;
using Catalogo.Features.Products;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Catalogo.Tests;

/// <summary>
/// Tela de listagem do acervo, UI-04 (T-17), e as setas de reposicionamento que T-15
/// deixou sem onde morar.
///
/// Cada caso recebe um **banco próprio** no container compartilhado. A tela desenha o
/// acervo inteiro, então o estado `vazio` só é alcançável com um acervo de fato vazio — e
/// o `buscaSemResultado`, com um recorte que não encontra nada. Compartilhando o banco da
/// coleção, os dois estados seriam inverificáveis.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProductListScreenTests(PostgresFixture postgres) : IAsyncLifetime, IDisposable
{
    private const string OwnerUserName = "dono-listagem";
    private const string OwnerPassword = "Catalogo!2026";
    private const string ListRoute = "/painel/produtos";

    private string connectionString = string.Empty;
    private WebApplicationFactory<Program> factory = null!;

    public async Task InitializeAsync()
    {
        var databaseName = $"lista_{Guid.NewGuid():N}";

        await using var admin = new NpgsqlConnection(postgres.ConnectionString);
        await admin.OpenAsync();

        await using var create = admin.CreateCommand();
        create.CommandText = $"""CREATE DATABASE "{databaseName}" """;
        await create.ExecuteNonQueryAsync();

        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
        {
            Database = databaseName
        }.ConnectionString;

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:UserName", OwnerUserName);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:Password", OwnerPassword);
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory?.Dispose();

    [Fact]
    public async Task A_listagem_exige_autenticacao()
    {
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(ListRoute);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task UI_04_vazio_chama_para_cadastrar_o_primeiro_produto()
    {
        using var client = await SignedInClientAsync();

        var html = await client.GetStringAsync(ListRoute);

        Assert.Contains("""data-estado="vazio" """.TrimEnd(), html);
        Assert.Contains("Cadastrar o primeiro produto", html);
    }

    [Fact]
    public async Task UI_04_default_agrupa_por_categoria_com_a_numeracao_posicional()
    {
        await SeedAsync();
        using var client = await SignedInClientAsync();

        var html = await client.GetStringAsync(ListRoute);

        Assert.Contains("""data-estado="default" """.TrimEnd(), html);
        Assert.Contains("Impressoras", html);
        Assert.Contains("Energia", html);
        Assert.Contains("""<span class="catbar__n">01</span>""", html);
        Assert.Contains("""<span class="catbar__n">02</span>""", html);
    }

    /// <summary>
    /// A RN-15 é explícita: o Rascunho aparece no painel e só nele. Aqui ele precisa ser
    /// distinguível do publicado, não apenas presente.
    /// </summary>
    [Fact]
    public async Task UI_04_distingue_o_rascunho_do_publicado()
    {
        await SeedAsync();
        using var client = await SignedInClientAsync();

        var html = await client.GetStringAsync(ListRoute);

        Assert.Contains("linha--rascunho", html);
        Assert.Contains("selo--rascunho", html);
        Assert.Contains("selo--no-ar", html);
    }

    [Fact]
    public async Task UI_04_exibe_marcador_de_falta_no_produto_sem_foto()
    {
        await SeedAsync();
        using var client = await SignedInClientAsync();

        var html = await client.GetStringAsync(ListRoute);

        Assert.Contains("""data-estado="semFoto" """.TrimEnd(), html);
        Assert.Contains("miniatura--ausente", html);
        Assert.Contains("sem foto", html);
    }

    [Fact]
    public async Task UI_04_filtra_por_situacao_pela_url()
    {
        await SeedAsync();
        using var client = await SignedInClientAsync();

        var published = await client.GetStringAsync($"{ListRoute}?situacao=no-ar");
        var drafts = await client.GetStringAsync($"{ListRoute}?situacao=rascunhos");

        Assert.Contains("Multifuncional", published);
        Assert.DoesNotContain("Nobreak", published);

        Assert.Contains("Nobreak", drafts);
        Assert.DoesNotContain("Multifuncional", drafts);
    }

    [Fact]
    public async Task UI_04_buscaSemResultado_quando_o_recorte_nao_encontra_nada()
    {
        await SeedAsync(publishAnything: false);
        using var client = await SignedInClientAsync();

        var html = await client.GetStringAsync($"{ListRoute}?situacao=no-ar");

        Assert.Contains("""data-estado="buscaSemResultado" """.TrimEnd(), html);
        Assert.Contains("Nenhum produto nesta situação.", html);
    }

    /// <summary>
    /// Quarto critério de T-15: a tela precisa dizer que a ordem é única, porque a
    /// consequência da ADR-015 — mover aqui move em todos os recortes — não é visível.
    /// </summary>
    [Fact]
    public async Task UI_04_informa_que_a_ordem_vale_para_todos_os_canais()
    {
        await SeedAsync();
        using var client = await SignedInClientAsync();

        // O texto vem de expressão C#, e o HtmlEncoder escapa os acentos como entidades
        // numéricas — decodificar é o que torna a asserção sobre a frase, e não sobre a
        // codificação de saída.
        var html = WebUtility.HtmlDecode(await client.GetStringAsync(ListRoute));

        Assert.Contains("A ordem é uma só", html);
        Assert.Contains("Não existe ordem por catálogo.", html);
    }

    /// <summary>
    /// Segundo critério de T-15, do lado da tela: os extremos da categoria não oferecem a
    /// ação. A primeira linha tem a seta de subir desabilitada, a última a de descer.
    /// </summary>
    [Fact]
    public async Task UI_04_desabilita_as_setas_nos_extremos_da_categoria()
    {
        await SeedAsync();
        using var client = await SignedInClientAsync();

        var html = await client.GetStringAsync(ListRoute);

        Assert.Matches("""aria-label="Subir Multifuncional"[^>]*disabled""", html);
        Assert.Matches("""aria-label="Descer Scanner"[^>]*disabled""", html);
        Assert.DoesNotMatch("""aria-label="Descer Multifuncional"[^>]*disabled""", html);
    }

    /// <summary>
    /// Duas categorias, com a de posição 1 trazendo dois produtos — um publicado com foto
    /// e um rascunho sem foto — e a de posição 2 trazendo um rascunho.
    /// </summary>
    private async Task SeedAsync(bool publishAnything = true)
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var printers = new Category { Name = "Impressoras", Position = 1 };
        var power = new Category { Name = "Energia", Position = 2 };
        context.Categories.AddRange(printers, power);
        await context.SaveChangesAsync();

        context.Products.AddRange(
            new Product
            {
                Name = "Multifuncional",
                Price = 1349.90m,
                CategoryId = printers.Id,
                Position = 1,
                Status = publishAnything ? ProductStatus.Published : ProductStatus.Draft,
                Photo = Photo()
            },
            new Product
            {
                Name = "Scanner",
                Price = 899.00m,
                CategoryId = printers.Id,
                Position = 2,
                Status = ProductStatus.Draft
            },
            new Product
            {
                Name = "Nobreak",
                Price = 549.00m,
                CategoryId = power.Id,
                Position = 1,
                Status = ProductStatus.Draft
            });

        await context.SaveChangesAsync();
    }

    private static ProductPhoto Photo()
    {
        var prefix = Guid.NewGuid().ToString("N");

        return new ProductPhoto
        {
            OriginalFileName = $"{prefix}-original.webp",
            ThumbnailFileName = $"{prefix}-miniatura.webp",
            CardFileName = $"{prefix}-cartao.webp",
            LargeFileName = $"{prefix}-ampliada.webp",
            PrintFileName = $"{prefix}-impressao.jpg"
        };
    }

    private CatalogDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connectionString).Options);

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateDefaultClient(new Uri("https://localhost"), new CookieHandler());

        var page = await client.GetStringAsync(PanelAuthentication.LoginPath);
        var token = Regex.Match(
            page,
            """name="__RequestVerificationToken"[^>]*value="([^"]+)""").Groups[1].Value;

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "acesso",
            ["Input.UserName"] = OwnerUserName,
            ["Input.Password"] = OwnerPassword,
            ["__RequestVerificationToken"] = token
        });

        using var response = await client.PostAsync(PanelAuthentication.LoginPath, form);
        response.EnsureSuccessStatusCode();

        return client;
    }
}
