using System.Net;
using Catalogo.Data;
using Catalogo.Features.Categories;
using Catalogo.Features.Products;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Catalogo.Tests;

/// <summary>
/// Listagem da vitrine, UI-01 (T-19). O que se verifica aqui é o que um **anônimo** vê:
/// nenhum caso faz login, de propósito — a RN-47 diz que a vitrine é totalmente anônima, e
/// um teste autenticado não provaria isso.
///
/// Banco próprio por caso: a tela desenha o acervo publicado inteiro, e o estado `vazio`
/// só existe com acervo de fato vazio.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StorefrontScreenTests(PostgresFixture postgres) : IAsyncLifetime, IDisposable
{
    private IsolatedDatabase? database;
    private string connectionString = string.Empty;
    private WebApplicationFactory<Program> factory = null!;

    public async Task InitializeAsync()
    {
        database = await IsolatedDatabase.CreateAsync(postgres, "loja");
        connectionString = database.ConnectionString;

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Default", connectionString));
    }

    public async Task DisposeAsync()
    {
        if (database is not null)
        {
            await database.DisposeAsync();
        }
    }

    public void Dispose() => factory?.Dispose();

    [Fact]
    public async Task CA_21_visitante_anonimo_recebe_a_listagem_sem_login()
    {
        await SeedAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Impressora EPSON L3250", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// CA-21, segunda metade. A RN-47 é uma lista de ausências — carrinho, estoque,
    /// pedido, login —, e ausência só se verifica procurando.
    /// </summary>
    [Fact]
    public async Task CA_21_nenhum_elemento_de_carrinho_estoque_ou_login_aparece()
    {
        await SeedAsync();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        foreach (var proibido in new[]
        {
            "carrinho", "Carrinho", "estoque", "Estoque", "Entrar", "Cadastre-se",
            "Adicionar", "Comprar agora", "unidades disponíveis"
        })
        {
            Assert.DoesNotContain(proibido, html);
        }
    }

    /// <summary>
    /// O critério mais fácil de quebrar sem perceber: um `@@rendermode` em qualquer lugar
    /// da árvore e a vitrine passa a abrir circuito, perdendo o cache de T-21 e a meta de
    /// LCP em 4G. O marcador de circuito no HTML é a evidência (ADR-010).
    /// </summary>
    [Fact]
    public async Task A_vitrine_nao_abre_conexao_persistente()
    {
        await SeedAsync();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.DoesNotContain("\"type\":\"server\"", html);
        Assert.DoesNotContain("\"type\":\"webassembly\"", html);
    }

    [Fact]
    public async Task CA_23_categoria_aparece_com_a_contagem_de_produtos()
    {
        await SeedAsync();
        using var client = factory.CreateClient();

        // O nome vem de expressão C#, e o HtmlEncoder escapa acento e `&` — decodificar
        // torna a asserção sobre a contagem exibida, não sobre a codificação de saída.
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/"));

        Assert.Matches("""Periféricos & Acessórios</span>\s*<span class="categoria__n">2</span>""", html);
    }

    [Fact]
    public async Task CA_23_selecionar_a_categoria_restringe_a_grade()
    {
        var seeded = await SeedAsync();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync($"/?categoria={seeded.Peripherals}");

        Assert.Contains("Monitor VXPro 19", html);
        Assert.DoesNotContain("Impressora EPSON L3250", html);
        Assert.Contains("""data-estado="filtrado" """.TrimEnd(), html);
    }

    /// <summary>
    /// CA-24: a URL é o estado. Duas requisições independentes com a mesma URL têm de
    /// devolver a mesma listagem — é o que "abrir em outro dispositivo" significa quando
    /// não há sessão nem cookie envolvidos (RN-56).
    /// </summary>
    [Fact]
    public async Task CA_24_a_mesma_url_reproduz_a_mesma_listagem_filtrada_e_paginada()
    {
        var seeded = await SeedAsync();
        var url = $"/?categoria={seeded.Printers}&pagina=2";

        using var first = factory.CreateClient();
        using var second = factory.CreateClient();

        var um = await first.GetStringAsync(url);
        var outro = await second.GetStringAsync(url);

        Assert.Equal(Grade(um), Grade(outro));
        Assert.Contains("Impressora HP Smart Tank 210", um);
        Assert.DoesNotContain("Impressora EPSON L3250", um);
    }

    [Fact]
    public async Task RN_56_busca_categoria_e_pagina_aparecem_na_url_dos_links()
    {
        var seeded = await SeedAsync();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/?busca=impressora");

        Assert.Contains($"/?busca=impressora&amp;categoria={seeded.Printers}", html);
    }

    [Fact]
    public async Task UI_01_busca_sem_resultado_mostra_o_termo_e_o_caminho_de_volta()
    {
        await SeedAsync();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/?busca=impressora%20a%20laser");

        Assert.Contains("""data-estado="buscaSemResultado" """.TrimEnd(), html);
        Assert.Contains("impressora a laser", html);
        Assert.Contains("Ver catálogo completo", html);
    }

    /// <summary>
    /// A SPEC-UI pede estado neutro, "sem sugerir erro". Um acervo em Rascunho é o caso
    /// real: existe produto cadastrado, e nenhum publicado.
    /// </summary>
    [Fact]
    public async Task UI_01_vazio_e_neutro_quando_nada_esta_publicado()
    {
        await SeedAsync(publish: false);
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("""data-estado="vazio" """.TrimEnd(), html);
        Assert.Contains("ainda está sendo montado", html);
        Assert.DoesNotContain("Erro", html);
    }

    /// <summary>
    /// Submeter a busca vazia manda `?busca=`, e string em branco não é filtro: sem
    /// normalizar, o estado de primeiro uso ficaria escondido atrás de um filtro que não
    /// existe.
    /// </summary>
    [Fact]
    public async Task Busca_em_branco_nao_conta_como_filtro()
    {
        await SeedAsync(publish: false);
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/?busca=");

        Assert.Contains("""data-estado="vazio" """.TrimEnd(), html);
    }

    /// <summary>
    /// Risco declarado em T-19 e observação da SPEC-UI: prometer busca ampla e entregar
    /// busca por nome é pior que anunciar o escopo real. O protótipo dizia "Buscar produto
    /// ou marca", e marca foi descartada na lacuna 1.
    /// </summary>
    [Fact]
    public async Task O_campo_de_busca_nao_promete_mais_do_que_a_busca_faz()
    {
        await SeedAsync();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Buscar pelo nome do produto", html);
        Assert.DoesNotContain("ou marca", html);
    }

    [Fact]
    public async Task RN_48_produto_em_rascunho_nao_consta_da_grade()
    {
        await SeedAsync();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.DoesNotContain("Nobreak em rascunho", html);
        Assert.DoesNotContain("Rascunho", html);
    }

    private static string Grade(string html)
    {
        var start = html.IndexOf("""<div class="grade">""", StringComparison.Ordinal);
        var end = html.IndexOf("</nav>", start, StringComparison.Ordinal);

        return html[start..end];
    }

    /// <summary>
    /// Quatro impressoras — o que dá duas páginas com o tamanho padrão de 12 apenas se a
    /// página for pedida menor, então a paginação por categoria usa o acervo real: são 4
    /// itens em Impressoras e o teste de CA-24 pede `pagina=2` com tamanho padrão. Para
    /// isso valer, o recorte precisa de mais de 12 itens na categoria.
    /// </summary>
    private async Task<(int Printers, int Peripherals)> SeedAsync(bool publish = true)
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var printers = new Category { Name = "Impressoras", Position = 1 };
        var peripherals = new Category { Name = "Periféricos & Acessórios", Position = 2 };
        var power = new Category { Name = "Energia & Proteção", Position = 3 };
        context.Categories.AddRange(printers, peripherals, power);
        await context.SaveChangesAsync();

        var status = publish ? ProductStatus.Published : ProductStatus.Draft;
        var position = 1;

        // Treze impressoras: doze enchem a primeira página e a décima terceira força a
        // segunda, que é o que o CA-24 precisa para ter "página 2" de verdade.
        context.Products.Add(Product("Impressora EPSON L3250", printers.Id, position++, status));

        for (var extra = 1; extra <= 11; extra++)
        {
            context.Products.Add(Product($"Impressora de enchimento {extra:00}", printers.Id, position++, status));
        }

        context.Products.Add(Product("Impressora HP Smart Tank 210", printers.Id, position++, status));

        context.Products.Add(Product("Monitor VXPro 19 WXGA+", peripherals.Id, 1, status));
        context.Products.Add(Product("Headset HP DHH-1601", peripherals.Id, 2, status));

        context.Products.Add(Product("Nobreak em rascunho", power.Id, 1, ProductStatus.Draft));

        await context.SaveChangesAsync();

        return (printers.Id, peripherals.Id);
    }

    private static Product Product(string name, int categoryId, int position, ProductStatus status) =>
        new()
        {
            Name = name,
            Summary = "Resumo curto que alimenta o card",
            Price = 1470.00m,
            CategoryId = categoryId,
            Position = position,
            Status = status,
            Photo = Photo()
        };

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
}
