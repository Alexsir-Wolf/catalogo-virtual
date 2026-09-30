using Catalogo.Data;
using Catalogo.Features.Categories;
using Catalogo.Features.Media;
using Catalogo.Features.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace Catalogo.Tests;

/// <summary>
/// O caminho de erro do envio. Aqui o armazenamento é simulado de propósito — o que se
/// prova não é a gravação, e sim que a falha dela chega à tela como mensagem em vez de
/// subir como exceção e derrubar o circuito com o formulário digitado (ADR-010, ADR-018).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProductPhotoUploadFailureTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Falha_do_armazenamento_vira_mensagem_e_preserva_a_foto_anterior()
    {
        var maintenance = CreateMaintenance();
        var productId = await CreateProductAsync(maintenance);
        var previous = await maintenance.AttachPhotoAsync(productId, PhotoNamed("anterior"));

        var storage = new FakeObjectStorage(fails: true);
        var upload = CreateUpload(storage, maintenance);

        using var image = CreateImage(400, 300);
        var outcome = await upload.StoreAsync(productId, image, image.Length);

        Assert.Equal(ProductPhotoUpload.StorageUnavailableMessage, outcome.Error);

        // A tela mantém a foto anterior porque o resultado não traz foto nova.
        Assert.Null(outcome.Photo);

        var stored = await maintenance.FindPhotoAsync(productId);

        Assert.Equal(previous!.CardFileName, stored!.CardFileName);
    }

    [Fact]
    public async Task Produto_inexistente_nao_apaga_a_foto_da_tela_sem_dizer_nada()
    {
        var maintenance = CreateMaintenance();
        var storage = new FakeObjectStorage(fails: false);
        var upload = CreateUpload(storage, maintenance);

        using var image = CreateImage(400, 300);
        var outcome = await upload.StoreAsync(int.MaxValue, image, image.Length);

        Assert.Equal(ProductPhotoUpload.ProductUnavailableMessage, outcome.Error);
        Assert.Null(outcome.Photo);
    }

    /// <summary>
    /// RN-10: o tamanho é decidido pela validação, não pelo leitor do circuito. A recusa
    /// acontece antes de qualquer leitura, então nada é gravado.
    /// </summary>
    [Fact]
    public async Task Arquivo_acima_do_limite_e_recusado_por_tamanho_antes_de_qualquer_gravacao()
    {
        var maintenance = CreateMaintenance();
        var storage = new FakeObjectStorage(fails: false);
        var upload = CreateUpload(storage, maintenance);

        using var image = CreateImage(400, 300);
        var outcome = await upload.StoreAsync(
            int.MaxValue,
            image,
            ImageValidation.MaxFileSizeInBytes + 1);

        Assert.Contains($"{ImageValidation.MaxFileSizeInBytes / (1024 * 1024)} MB", outcome.Error!);
        Assert.Equal(0, storage.Uploads);
    }

    private static ProductPhotoUpload CreateUpload(
        IObjectStorage storage,
        ProductMaintenance maintenance)
    {
        var options = Options.Create(new ObjectStorageOptions
        {
            Url = "https://armazenamento.invalido",
            ServiceKey = "chave-de-teste"
        });

        return new ProductPhotoUpload(
            new ProductPhotoService(
                storage,
                new ImageProcessor(),
                options,
                NullLogger<ProductPhotoService>.Instance),
            maintenance,
            NullLogger<ProductPhotoUpload>.Instance);
    }

    private static ProductPhoto PhotoNamed(string root) => new()
    {
        OriginalFileName = $"{root}-original",
        ThumbnailFileName = NameFor(root, ImageDerivative.Thumbnail),
        CardFileName = NameFor(root, ImageDerivative.Card),
        LargeFileName = NameFor(root, ImageDerivative.Large),
        PrintFileName = NameFor(root, ImageDerivative.Print)
    };

    private static string NameFor(string root, ImageDerivative derivative) =>
        ImageProcessor.ObjectNameFor(
            root,
            DerivativeSpecifications.All.Single(spec => spec.Derivative == derivative));

    private async Task<int> CreateProductAsync(ProductMaintenance maintenance)
    {
        await using var context = postgres.CreateContext();

        var category = new Category { Name = $"Categoria {Guid.NewGuid():N}", Position = 1 };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var outcome = await maintenance.SaveAsync(new ProductDraft
        {
            Name = "Monitor VXPro 19",
            Price = 599.90m,
            CategoryId = category.Id
        });

        return outcome.Id!.Value;
    }

    private static MemoryStream CreateImage(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Goldenrod);

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        return new MemoryStream(encoded.ToArray());
    }

    private ProductMaintenance CreateMaintenance() =>
        new(new ContextFactory(postgres.ConnectionString), TestCache.Silent(), TimeProvider.System, NullLogger<ProductMaintenance>.Instance);

    /// <summary>
    /// CA-08, a metade dos arquivos: a exclusão remove o original **e as quatro derivadas**,
    /// cada um do bucket em que vive — a de impressão é privada (RN-12), as três de tela são
    /// públicas. Sem este caso, esquecer uma derivada passaria sem ninguém ver.
    /// </summary>
    [Fact]
    public async Task CA_08_excluir_remove_o_original_e_as_quatro_derivadas()
    {
        var maintenance = CreateMaintenance();
        var productId = await CreateProductAsync(maintenance);
        var photo = await maintenance.AttachPhotoAsync(productId, PhotoNamed("excluida"));

        var storage = new FakeObjectStorage(fails: false);
        var removed = await maintenance.DeleteAsync(productId);

        await CreatePhotoService(storage).DeleteAsync(removed!);

        Assert.Equal(5, storage.Deleted.Count);
        Assert.Contains(photo!.OriginalFileName, storage.Deleted);
        Assert.Contains(photo.ThumbnailFileName, storage.Deleted);
        Assert.Contains(photo.CardFileName, storage.Deleted);
        Assert.Contains(photo.LargeFileName, storage.Deleted);
        Assert.Contains(photo.PrintFileName, storage.Deleted);
    }

    /// <summary>
    /// Falha em um objeto **não interrompe os outros**: o registro já saiu do acervo, e parar
    /// no meio deixaria mais arquivos órfãos, não menos.
    /// </summary>
    [Fact]
    public async Task Falha_ao_remover_um_objeto_nao_interrompe_a_limpeza_dos_outros()
    {
        var maintenance = CreateMaintenance();
        var productId = await CreateProductAsync(maintenance);
        await maintenance.AttachPhotoAsync(productId, PhotoNamed("parcial"));

        var storage = new FakeObjectStorage(fails: false, failsDeletion: true);
        var removed = await maintenance.DeleteAsync(productId);

        await CreatePhotoService(storage).DeleteAsync(removed!);

        Assert.Equal(5, storage.DeleteAttempts);
    }

    private static ProductPhotoService CreatePhotoService(IObjectStorage storage) =>
        new(
            storage,
            new ImageProcessor(),
            Options.Create(new ObjectStorageOptions
            {
                Url = "https://armazenamento.invalido",
                ServiceKey = "chave-de-teste"
            }),
            NullLogger<ProductPhotoService>.Instance);

    private sealed class ContextFactory(string connectionString) : IDbContextFactory<CatalogDbContext>
    {
        public CatalogDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connectionString).Options);
    }

    /// <summary>
    /// Armazenamento que falha como o Supabase falha na prática: a resposta de erro vira
    /// <see cref="HttpRequestException"/> no <c>EnsureSuccessStatusCode</c>.
    /// </summary>
    private sealed class FakeObjectStorage(bool fails, bool failsDeletion = false) : IObjectStorage
    {
        private readonly List<string> deleted = [];

        public int Uploads { get; private set; }

        /// <summary>Nomes removidos com sucesso.</summary>
        public IReadOnlyList<string> Deleted => deleted;

        /// <summary>Tentativas de remoção, com ou sem sucesso.</summary>
        public int DeleteAttempts { get; private set; }

        public Task UploadAsync(
            string bucket,
            string objectName,
            byte[] content,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            Uploads++;

            return fails
                ? throw new HttpRequestException("O armazenamento respondeu 503.")
                : Task.CompletedTask;
        }

        public Task DeleteAsync(
            string bucket,
            string objectName,
            CancellationToken cancellationToken = default)
        {
            DeleteAttempts++;

            if (failsDeletion)
            {
                throw new HttpRequestException("O armazenamento respondeu 503.");
            }

            deleted.Add(objectName);

            return Task.CompletedTask;
        }

        public Task<byte[]> DownloadAsync(
            string bucket,
            string objectName,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public string PublicUrlFor(string objectName) => objectName;
    }
}
