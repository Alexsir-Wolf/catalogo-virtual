using Catalogo.Data;
using Catalogo.Features.Categories;
using Catalogo.Features.Media;
using Catalogo.Features.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Catalogo.Tests;

/// <summary>
/// Exclusão definitiva (RN-20, CA-08) pelo mesmo ponto de entrada que a tela usa.
///
/// A classe existe porque a **ordem** das operações é a decisão de T-16, e enquanto ela vivia
/// no manipulador de evento do componente nada a protegia: inverter as duas chamadas deixava a
/// suíte verde (achado do review de T-16).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProductRemovalTests(PostgresFixture postgres)
{
    /// <summary>
    /// CA-08 pelas duas metades: o produto sai do acervo **e** os cinco objetos são removidos.
    /// </summary>
    [Fact]
    public async Task CA_08_exclui_o_registro_e_remove_os_cinco_objetos()
    {
        var maintenance = CreateMaintenance();
        var productId = await CreateProductAsync();
        await maintenance.AttachPhotoAsync(productId, PhotoNamed("removida"));

        var storage = new RecordingStorage();
        var outcome = await CreateRemoval(maintenance, storage).RemoveAsync(productId);

        Assert.True(outcome.Succeeded);
        Assert.True(outcome.HadPhoto);
        Assert.Equal(5, storage.Deleted.Count);
        Assert.Null(await maintenance.FindAsync(productId));
    }

    /// <summary>
    /// **A ordem é a decisão.** Se os arquivos fossem removidos antes do registro, uma falha de
    /// banco deixaria um produto no acervo sem imagem alguma, e a vitrine exibiria item
    /// quebrado. Este caso falha se alguém inverter a sequência.
    /// </summary>
    [Fact]
    public async Task O_registro_sai_antes_dos_arquivos()
    {
        var maintenance = CreateMaintenance();
        var productId = await CreateProductAsync();
        await maintenance.AttachPhotoAsync(productId, PhotoNamed("ordem"));

        // O armazenamento consulta o banco no momento em que recebe a primeira remoção: se o
        // registro ainda estivesse lá, a ordem estaria invertida.
        var storage = new RecordingStorage(async () =>
        {
            await using var context = postgres.CreateContext();

            return await context.Products.AnyAsync(product => product.Id == productId);
        });

        await CreateRemoval(maintenance, storage).RemoveAsync(productId);

        Assert.False(storage.ProductStillExistedOnFirstDelete);
    }

    /// <summary>
    /// Cada objeto precisa ser removido **do bucket em que vive**: a derivada de impressão e o
    /// original são privados (RN-12), as três de tela são públicas. Bucket errado faz o DELETE
    /// responder 404, o erro é engolido em log, e o objeto fica pago para sempre.
    /// </summary>
    [Fact]
    public async Task RN_12_cada_objeto_e_removido_do_bucket_em_que_vive()
    {
        var maintenance = CreateMaintenance();
        var productId = await CreateProductAsync();
        var photo = await maintenance.AttachPhotoAsync(productId, PhotoNamed("buckets"));

        var storage = new RecordingStorage();
        await CreateRemoval(maintenance, storage).RemoveAsync(productId);

        Assert.Contains((PrivateBucket, photo!.OriginalFileName), storage.Deleted);
        Assert.Contains((PrivateBucket, photo.PrintFileName), storage.Deleted);
        Assert.Contains((PublicBucket, photo.ThumbnailFileName), storage.Deleted);
        Assert.Contains((PublicBucket, photo.CardFileName), storage.Deleted);
        Assert.Contains((PublicBucket, photo.LargeFileName), storage.Deleted);
    }

    /// <summary>
    /// O caso que o review de T-16 apontou como bloqueante: o timeout do armazenamento chega
    /// como `TaskCanceledException`, que **é** um `OperationCanceledException` — o filtro de
    /// `catch` anterior o deixava escapar, o `foreach` abortava no primeiro objeto e a exceção
    /// subia até derrubar o circuito, **depois** de o produto já ter sido excluído.
    /// </summary>
    [Fact]
    public async Task Timeout_do_armazenamento_nao_interrompe_a_limpeza_nem_escapa()
    {
        var maintenance = CreateMaintenance();
        var productId = await CreateProductAsync();
        await maintenance.AttachPhotoAsync(productId, PhotoNamed("timeout"));

        var storage = new RecordingStorage { FailWith = new TaskCanceledException() };

        var outcome = await CreateRemoval(maintenance, storage).RemoveAsync(productId);

        Assert.True(outcome.Succeeded);
        Assert.Equal(5, storage.Attempts);
        Assert.Null(await maintenance.FindAsync(productId));
    }

    [Fact]
    public async Task Falha_de_transporte_tambem_nao_interrompe_a_limpeza()
    {
        var maintenance = CreateMaintenance();
        var productId = await CreateProductAsync();
        await maintenance.AttachPhotoAsync(productId, PhotoNamed("transporte"));

        var storage = new RecordingStorage { FailWith = new HttpRequestException("503") };

        await CreateRemoval(maintenance, storage).RemoveAsync(productId);

        Assert.Equal(5, storage.Attempts);
    }

    [Fact]
    public async Task Produto_sem_foto_e_excluido_sem_tocar_o_armazenamento()
    {
        var maintenance = CreateMaintenance();
        var productId = await CreateProductAsync();

        var storage = new RecordingStorage();
        var outcome = await CreateRemoval(maintenance, storage).RemoveAsync(productId);

        Assert.True(outcome.Succeeded);
        Assert.False(outcome.HadPhoto);
        Assert.Equal(0, storage.Attempts);
    }

    [Fact]
    public async Task Produto_inexistente_nao_e_erro_para_o_dono()
    {
        var outcome = await CreateRemoval(CreateMaintenance(), new RecordingStorage())
            .RemoveAsync(987654);

        Assert.True(outcome.Succeeded);
        Assert.False(outcome.HadPhoto);
    }

    private const string PublicBucket = "publico";
    private const string PrivateBucket = "privado";

    private ProductRemoval CreateRemoval(ProductMaintenance maintenance, IObjectStorage storage) =>
        new(
            maintenance,
            new ProductPhotoService(
                storage,
                new ImageProcessor(),
                Options.Create(new ObjectStorageOptions
                {
                    Url = "https://armazenamento.invalido",
                    ServiceKey = "chave-de-teste",
                    PublicBucket = PublicBucket,
                    PrivateBucket = PrivateBucket
                }),
                NullLogger<ProductPhotoService>.Instance),
            NullLogger<ProductRemoval>.Instance);

    private static ProductPhoto PhotoNamed(string root)
    {
        var prefix = $"{root}-{Guid.NewGuid():N}";

        return new ProductPhoto
        {
            OriginalFileName = $"{prefix}-original",
            ThumbnailFileName = $"{prefix}-miniatura.webp",
            CardFileName = $"{prefix}-cartao.webp",
            LargeFileName = $"{prefix}-ampliada.webp",
            PrintFileName = $"{prefix}-impressao.jpg"
        };
    }

    private async Task<int> CreateProductAsync()
    {
        await using var context = postgres.CreateContext();

        var category = new Category { Name = $"Categoria {Guid.NewGuid():N}", Position = 1 };
        context.Categories.Add(category);

        var product = new Product
        {
            Name = "Cabo HDMI Ugreen",
            Price = 49.90m,
            Category = category,
            Position = 1,
            Status = ProductStatus.Published
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        return product.Id;
    }

    private ProductMaintenance CreateMaintenance() =>
        new(new ContextFactory(postgres.ConnectionString), TestCache.Silent(), TimeProvider.System, NullLogger<ProductMaintenance>.Instance);

    private sealed class ContextFactory(string connectionString) : IDbContextFactory<CatalogDbContext>
    {
        public CatalogDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connectionString).Options);
    }

    /// <summary>
    /// Armazenamento que registra o par **bucket e nome** — sem o bucket, um DELETE no bucket
    /// errado passaria no teste e viraria objeto órfão em produção.
    /// </summary>
    private sealed class RecordingStorage(Func<Task<bool>>? onFirstDelete = null) : IObjectStorage
    {
        private readonly List<(string Bucket, string ObjectName)> deleted = [];

        public IReadOnlyList<(string Bucket, string ObjectName)> Deleted => deleted;

        public int Attempts { get; private set; }

        public bool ProductStillExistedOnFirstDelete { get; private set; }

        public Exception? FailWith { get; init; }

        public Task UploadAsync(
            string bucket,
            string objectName,
            byte[] content,
            string contentType,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task DeleteAsync(
            string bucket,
            string objectName,
            CancellationToken cancellationToken = default)
        {
            if (Attempts == 0 && onFirstDelete is not null)
            {
                ProductStillExistedOnFirstDelete = await onFirstDelete();
            }

            Attempts++;

            if (FailWith is not null)
            {
                throw FailWith;
            }

            deleted.Add((bucket, objectName));
        }

        public Task<byte[]> DownloadAsync(
            string bucket,
            string objectName,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public string PublicUrlFor(string objectName) => objectName;
    }
}
