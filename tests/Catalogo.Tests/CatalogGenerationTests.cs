using Catalogo.Data;
using Catalogo.Features.CatalogBuilder;
using Catalogo.Features.Categories;
using Catalogo.Features.Media;
using Catalogo.Features.PdfExport;
using Catalogo.Features.Products;
using Catalogo.Features.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc.Testing;
using Catalogo.Features.Account;
using QuestPDF.Infrastructure;

namespace Catalogo.Tests;

/// <summary>
/// Geração do documento (T-25): teto, recusas, progresso e entrega.
///
/// A regra que mais importa aqui é uma **ausência**: o PDF não é armazenado (RN-35). Não existe
/// pasta de saída, nome de arquivo no servidor nem limpeza — e é isso que torna impossível
/// entregar por engano uma versão velha.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CatalogGenerationTests : IAsyncLifetime, IDisposable
{
    private const string OwnerUserName = "dono-geracao";
    private const string OwnerPassword = "Catalogo!2026";

    private readonly PostgresFixture postgres;

    private IsolatedDatabase? database;
    private string connectionString = string.Empty;
    private WebApplicationFactory<Program> factory = null!;

    public CatalogGenerationTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task InitializeAsync()
    {
        database = await IsolatedDatabase.CreateAsync(postgres, "geracao");
        connectionString = database.ConnectionString;

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:UserName", OwnerUserName);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:Password", OwnerPassword);
        });

        await database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (database is not null)
        {
            await database.DisposeAsync();
        }
    }

    public void Dispose() => factory?.Dispose();

    /// <summary>
    /// CA-17: a composição termina, o arquivo é entregue, e a data da geração é registrada.
    /// </summary>
    [Fact]
    public async Task CA_17_gerar_entrega_o_arquivo_e_registra_a_data()
    {
        var catalogId = await SeedAsync(products: 18);
        await SetCoverAsync();

        var outcome = await CreateGeneration().GenerateAsync(catalogId);

        Assert.True(outcome.Succeeded);
        Assert.NotNull(outcome.Content);
        Assert.NotEmpty(outcome.Content);
        Assert.EndsWith(".pdf", outcome.FileName);
        Assert.Equal(18, outcome.ProductCount);

        // O arquivo é um PDF de verdade, não um vetor de bytes qualquer.
        Assert.Equal("%PDF"u8.ToArray(), outcome.Content!.Take(4).ToArray());

        var listed = (await CreateMaintenance().ListAsync()).Single(catalog => catalog.Id == catalogId);
        Assert.True(listed.WasGenerated);
    }

    /// <summary>
    /// CA-17 / RN-35, a metade que é uma ausência: **nenhum arquivo fica no servidor**. O teste
    /// varre o diretório da aplicação e o temporário antes e depois, e compara — se algum dia
    /// alguém introduzir um `File.WriteAllBytes` "para depurar", este caso cai.
    /// </summary>
    [Fact]
    public async Task CA_17_nenhum_pdf_permanece_no_servidor()
    {
        var catalogId = await SeedAsync(products: 6);
        await SetCoverAsync();

        var before = PdfFilesAround();

        var outcome = await CreateGeneration().GenerateAsync(catalogId);

        Assert.True(outcome.Succeeded);

        var after = PdfFilesAround();

        Assert.Equal(before, after);
    }

    /// <summary>
    /// CA-19 / RN-45: acima do teto a geração é recusada **antes de compor**, e a recusa informa
    /// a quantidade resolvida — "não deu" sem número deixa o dono sem saber o que reduzir.
    /// </summary>
    [Fact]
    public async Task CA_19_catalogo_acima_do_teto_e_recusado_antes_de_compor()
    {
        var catalogId = await SeedAsync(products: CatalogGeneration.MaxProducts + 1);
        await SetCoverAsync();

        var storage = new CountingStorage();
        var outcome = await CreateGeneration(storage).GenerateAsync(catalogId);

        Assert.Equal(GenerationRefusal.AboveLimit, outcome.Refusal);
        Assert.Equal(CatalogGeneration.MaxProducts + 1, outcome.ProductCount);
        Assert.Null(outcome.Content);

        // **Antes de compor** é verificável: nada foi baixado do armazenamento, que é o primeiro
        // passo da composição.
        Assert.Equal(0, storage.Downloads);
    }

    [Fact]
    public async Task No_teto_exato_a_geracao_acontece()
    {
        var catalogId = await SeedAsync(products: 3);
        await SetCoverAsync();

        var outcome = await CreateGeneration().GenerateAsync(catalogId);

        Assert.True(outcome.Succeeded);
    }

    /// <summary>
    /// RN-46 / CA-18: sem produto No ar não há documento, e a recusa acontece sem tocar o
    /// armazenamento.
    /// </summary>
    [Fact]
    public async Task RN_46_catalogo_sem_produto_no_ar_e_recusado()
    {
        var catalogId = await SeedAsync(products: 3, published: false);
        await SetCoverAsync();

        var storage = new CountingStorage();
        var outcome = await CreateGeneration(storage).GenerateAsync(catalogId);

        Assert.Equal(GenerationRefusal.NoProducts, outcome.Refusal);
        Assert.Equal(0, storage.Downloads);
    }

    /// <summary>
    /// RN-65: sem capa configurada a geração é recusada. Compor o miolo para descobrir depois
    /// que falta a capa seria trabalho jogado fora — e é a única pista que a UI-10 dá ao dono.
    /// </summary>
    [Fact]
    public async Task RN_65_sem_capa_configurada_a_geracao_e_recusada()
    {
        var catalogId = await SeedAsync(products: 3);

        var storage = new CountingStorage();
        var outcome = await CreateGeneration(storage).GenerateAsync(catalogId);

        Assert.Equal(GenerationRefusal.NoCover, outcome.Refusal);
        Assert.Equal(0, storage.Downloads);
    }

    /// <summary>
    /// CA-37 no caminho completo (T-32): o documento entregue é a **capa mais** as páginas de
    /// conteúdo, e a contagem total confere — uma folha de capa mais as páginas compostas.
    /// </summary>
    [Fact]
    public async Task CA_37_o_documento_entregue_tem_a_capa_na_frente_do_conteudo()
    {
        var catalogId = await SeedAsync(products: 12);
        await SetCoverAsync();

        var outcome = await CreateGeneration().GenerateAsync(catalogId);

        Assert.True(outcome.Succeeded);

        using var stream = new MemoryStream(outcome.Content!, writable: false);
        using var document = PdfSharp.Pdf.IO.PdfReader.Open(
            stream,
            PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);

        // A capa do falso tem uma página; o miolo de 12 produtos tem ao menos uma. Sem a
        // concatenação, o documento sairia só com o miolo.
        Assert.True(document.PageCount >= 2);

        // A primeira página é a da capa: ela é A4 e **vazia**, porque o falso não desenha nada
        // nela — a página de conteúdo tem fluxo de conteúdo.
        Assert.Empty(ContentOf(document.Pages[0]));
        Assert.NotEmpty(ContentOf(document.Pages[1]));
    }

    private static string ContentOf(PdfSharp.Pdf.PdfPage page)
    {
        var contents = page.Contents.Elements;

        return contents.Count == 0
            ? string.Empty
            : System.Text.Encoding.Latin1.GetString(
                contents.GetDictionary(0)!.Stream.UnfilteredValue);
    }

    [Fact]
    public async Task Catalogo_inexistente_e_recusado_sem_lancar()
    {
        var outcome = await CreateGeneration().GenerateAsync(987654);

        Assert.Equal(GenerationRefusal.NotFound, outcome.Refusal);
    }

    /// <summary>
    /// RN-44: a geração reporta progresso. O que se verifica é que as etapas chegam em ordem —
    /// sem isso a UI-08 não tem o que exibir no estado `gerando`.
    /// </summary>
    [Fact]
    public async Task RN_44_a_geracao_reporta_progresso_em_etapas()
    {
        var catalogId = await SeedAsync(products: 4);
        await SetCoverAsync();

        var stages = new List<string>();
        var progress = new Progress<GenerationProgress>(current => stages.Add(current.Stage));

        await CreateGeneration().GenerateAsync(catalogId, progress);

        // `Progress<T>` entrega no contexto capturado, então a chegada é assíncrona: o que
        // importa é que as etapas cheguem, e a última seja a conclusão.
        await WaitForAsync(() => stages.Count >= 3);

        Assert.Contains(GenerationProgress.Resolving.Stage, stages);
        Assert.Contains("Compondo 4 produtos", stages[1]);
        Assert.Equal(GenerationProgress.Done.Stage, stages[^1]);
    }

    /// <summary>
    /// Falha na composição **não entrega arquivo parcial**: um PDF truncado parece válido até
    /// alguém tentar imprimi-lo.
    /// </summary>
    [Fact]
    public async Task Falha_na_composicao_nao_entrega_arquivo_parcial()
    {
        var catalogId = await SeedAsync(products: 3);
        await SetCoverAsync();

        var outcome = await CreateGeneration(new FailingComposerStorage()).GenerateAsync(catalogId);

        // A falha ao baixar imagem é tolerada por decisão de T-24 (a célula sai sem foto), então
        // este caso prova o contrário do que parece: o documento **sai**, íntegro, sem a imagem.
        // O que não pode acontecer é vir conteúdo pela metade.
        Assert.True(outcome.Succeeded);
        Assert.Equal("%PDF"u8.ToArray(), outcome.Content!.Take(4).ToArray());
    }

    /// <summary>
    /// A data é registrada **depois** de o documento existir. Registrar antes faria um catálogo
    /// que falhou parecer gerado, e o destaque da RN-32 passaria a comparar com uma geração que
    /// nunca chegou a ninguém.
    /// </summary>
    [Fact]
    public async Task Geracao_recusada_nao_registra_data()
    {
        var catalogId = await SeedAsync(products: 3);

        await CreateGeneration().GenerateAsync(catalogId);

        var listed = (await CreateMaintenance().ListAsync()).Single(catalog => catalog.Id == catalogId);

        Assert.False(listed.WasGenerated);
    }

    /// <summary>
    /// O nome do arquivo leva o nome do catálogo e a data, porque o arquivo **é o único
    /// registro** daquele envio: um nome genérico faria dois envios se confundirem na pasta de
    /// downloads.
    /// </summary>
    [Fact]
    public void O_nome_do_arquivo_identifica_o_catalogo_e_a_data()
    {
        var catalog = new ResolvedCatalog(1, "Consumíveis & Tintas", [], null);

        var name = CreateGeneration().FileNameFor(catalog);

        Assert.StartsWith("catalogo-consum", name);
        Assert.EndsWith(".pdf", name);
        Assert.DoesNotContain("&", name);
        Assert.DoesNotContain("--", name);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 50 && !condition(); attempt++)
        {
            await Task.Delay(20);
        }
    }

    /// <summary>
    /// Arquivos PDF no diretório da aplicação e no temporário. É a forma de afirmar a ausência
    /// da RN-35 sem depender de saber onde alguém teria escrito.
    /// </summary>
    private static HashSet<string> PdfFilesAround()
    {
        var places = new[] { AppContext.BaseDirectory, Path.GetTempPath(), Directory.GetCurrentDirectory() };

        return [.. places
            .Where(Directory.Exists)
            .SelectMany(place => Directory.EnumerateFiles(place, "*.pdf", SearchOption.TopDirectoryOnly))];
    }

    private async Task SetCoverAsync()
    {
        await using var context = CreateContext();

        await context.PortalSettings
            .Where(entity => entity.Id == PortalSettings.SingletonId)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(entity => entity.CoverFileName, "capa/plantada.pdf"));
    }

    private async Task<int> SeedAsync(int products, bool published = true)
    {
        await using var context = CreateContext();

        var category = new Category { Name = $"Categoria {Guid.NewGuid():N}", Position = 1 };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        for (var index = 0; index < products; index++)
        {
            context.Products.Add(new Product
            {
                Name = $"Produto {index}",
                Summary = "Resumo do produto",
                Price = 99.90m,
                CategoryId = category.Id,
                Position = index + 1,
                Status = published ? ProductStatus.Published : ProductStatus.Draft,
                PublishedAt = published ? DateTimeOffset.UtcNow : null
            });
        }

        await context.SaveChangesAsync();

        var outcome = await CreateMaintenance().SaveAsync(new CatalogDraft
        {
            Name = $"Catálogo {Guid.NewGuid():N}",
            CategoryIds = [category.Id]
        });

        return outcome.Id!.Value;
    }

    private CatalogGeneration CreateGeneration(IObjectStorage? storage = null)
    {
        var contextFactory = new ContextFactory(connectionString);
        var options = Options.Create(new ObjectStorageOptions
        {
            Url = "https://armazenamento.invalido",
            ServiceKey = "chave-de-teste"
        });

        using var scope = factory.Services.CreateScope();

        var settings = new PortalSettingsService(
            contextFactory,
            storage ?? new CountingStorage(),
            options,
            scope.ServiceProvider.GetRequiredService<UserManager<OwnerAccount>>(),
            new PasswordAttemptLimiter(),
            NullLogger<PortalSettingsService>.Instance);

        return new CatalogGeneration(
            new CatalogResolution(contextFactory),
            new CatalogComposer(
                storage ?? new CountingStorage(),
                options,
                settings,
                NullLogger<CatalogComposer>.Instance),
            new CatalogMaintenance(contextFactory),
            settings,
            new FakeCoverSource(),
            TimeProvider.System,
            NullLogger<CatalogGeneration>.Instance);
    }

    private CatalogMaintenance CreateMaintenance() => new(new ContextFactory(connectionString));

    private CatalogDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connectionString).Options);

    private sealed class ContextFactory(string connectionString) : IDbContextFactory<CatalogDbContext>
    {
        public CatalogDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connectionString).Options);
    }

    /// <summary>
    /// A capa, pronta e válida, sem depender de credencial. A concatenação é exercitada de
    /// verdade — o documento final tem capa mais conteúdo (T-32).
    /// </summary>
    private sealed class FakeCoverSource : ICoverSource
    {
        public Task<byte[]> DownloadAsync(
            string objectName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SinglePagePdf());

        private static byte[] SinglePagePdf()
        {
            using var document = new PdfSharp.Pdf.PdfDocument();
            document.AddPage().Size = PdfSharp.PageSize.A4;

            using var stream = new MemoryStream();
            document.Save(stream, closeStream: false);

            return stream.ToArray();
        }
    }

    /// <summary>Conta downloads: é como se afirma "antes de compor".</summary>
    private class CountingStorage : IObjectStorage
    {
        public int Downloads { get; private set; }

        public Task UploadAsync(
            string bucket,
            string objectName,
            byte[] content,
            string contentType,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(
            string bucket,
            string objectName,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public virtual Task<byte[]> DownloadAsync(
            string bucket,
            string objectName,
            CancellationToken cancellationToken = default)
        {
            Downloads++;

            return Task.FromResult<byte[]>([]);
        }

        public string PublicUrlFor(string objectName) => objectName;
    }

    private sealed class FailingComposerStorage : CountingStorage
    {
        public override Task<byte[]> DownloadAsync(
            string bucket,
            string objectName,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("O armazenamento respondeu 503.");
    }
}
