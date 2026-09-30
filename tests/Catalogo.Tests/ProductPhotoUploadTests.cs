using Catalogo.Data;
using Catalogo.Features.Categories;
using Catalogo.Features.Media;
using Catalogo.Features.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Xunit;

namespace Catalogo.Tests;

/// <summary>
/// O caminho inteiro de T-13: imagem enviada, pipeline de T-08, derivadas gravadas no
/// armazenamento real e associadas ao produto. Sem credencial no ambiente estes testes
/// são pulados — provar isso com armazenamento simulado não provaria nada.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProductPhotoUploadTests(PostgresFixture postgres)
{
    [SkippableFact]
    public async Task Enviar_foto_grava_as_derivadas_e_associa_ao_produto()
    {
        var service = CreatePhotoService();
        var maintenance = CreateMaintenance();
        var productId = await CreateProductAsync(maintenance);

        using var image = CreateImage(1400, 1050);
        var result = await service.StoreAsync(image, image.Length);

        Assert.True(result.Succeeded);

        await maintenance.AttachPhotoAsync(productId, result.Photo!);
        var stored = await maintenance.FindPhotoAsync(productId);

        Assert.EndsWith("-thumbnail.webp", stored!.ThumbnailFileName);
        Assert.EndsWith("-card.webp", stored.CardFileName);
        Assert.EndsWith("-large.webp", stored.LargeFileName);
        Assert.EndsWith("-print.jpg", stored.PrintFileName);

        // As quatro derivadas compartilham a raiz imutável do envio (RN-13).
        var root = stored.CardFileName[..stored.CardFileName.IndexOf("-card", StringComparison.Ordinal)];

        Assert.StartsWith(root, stored.ThumbnailFileName);
        Assert.StartsWith(root, stored.LargeFileName);
        Assert.StartsWith(root, stored.PrintFileName);
    }

    [SkippableFact]
    public async Task CA_07_substituir_a_foto_aponta_para_derivadas_novas_no_armazenamento()
    {
        var service = CreatePhotoService();
        var maintenance = CreateMaintenance();
        var productId = await CreateProductAsync(maintenance);

        using var first = CreateImage(1200, 900);
        var before = await service.StoreAsync(first, first.Length);
        await maintenance.AttachPhotoAsync(productId, before.Photo!);

        using var second = CreateImage(1000, 1000);
        var after = await service.StoreAsync(second, second.Length);
        await maintenance.AttachPhotoAsync(productId, after.Photo!);

        var stored = await maintenance.FindPhotoAsync(productId);

        Assert.Equal(after.Photo!.CardFileName, stored!.CardFileName);
        Assert.NotEqual(before.Photo!.CardFileName, stored.CardFileName);

        // A derivada de tela nova responde publicamente; a referência antiga saiu do
        // produto, que é o que faz a vitrine parar de exibi-la.
        using var anonymous = new HttpClient();
        var response = await anonymous.GetAsync(service.PublicUrlFor(stored.CardFileName));

        Assert.True(response.IsSuccessStatusCode);
    }

    [SkippableFact]
    public async Task Arquivo_invalido_e_recusado_e_o_produto_mantem_a_foto_anterior()
    {
        var service = CreatePhotoService();
        var maintenance = CreateMaintenance();
        var productId = await CreateProductAsync(maintenance);

        using var valid = CreateImage(1200, 900);
        var accepted = await service.StoreAsync(valid, valid.Length);
        await maintenance.AttachPhotoAsync(productId, accepted.Photo!);

        using var notAnImage = new MemoryStream("isto não é uma imagem"u8.ToArray());
        var rejected = await service.StoreAsync(notAnImage, notAnImage.Length);

        Assert.False(rejected.Succeeded);
        Assert.Equal(ImageRejection.NotAnImage, rejected.Rejection);

        var stored = await maintenance.FindPhotoAsync(productId);

        Assert.Equal(accepted.Photo!.CardFileName, stored!.CardFileName);
    }

    private static ProductPhotoService CreatePhotoService()
    {
        var url = Environment.GetEnvironmentVariable("Supabase__Url");
        var serviceKey = Environment.GetEnvironmentVariable("Supabase__ServiceKey");

        Skip.If(
            string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(serviceKey),
            "Defina Supabase__Url e Supabase__ServiceKey para exercer o armazenamento real.");

        var options = new ObjectStorageOptions { Url = url!, ServiceKey = serviceKey! };

        if (Environment.GetEnvironmentVariable("Supabase__PublicBucket") is { Length: > 0 } publicBucket)
        {
            options.PublicBucket = publicBucket;
        }

        if (Environment.GetEnvironmentVariable("Supabase__PrivateBucket") is { Length: > 0 } privateBucket)
        {
            options.PrivateBucket = privateBucket;
        }

        var wrapped = Options.Create(options);

        return new ProductPhotoService(
            new SupabaseObjectStorage(new HttpClient(), wrapped),
            new ImageProcessor(),
            wrapped,
            NullLogger<ProductPhotoService>.Instance);
    }

    private async Task<int> CreateProductAsync(ProductMaintenance maintenance)
    {
        await using var context = postgres.CreateContext();

        var category = new Category { Name = $"Categoria {Guid.NewGuid():N}", Position = 1 };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var outcome = await maintenance.SaveAsync(new ProductDraft
        {
            Name = "Produto com foto",
            Price = 199.90m,
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
        new(new ContextFactory(postgres.ConnectionString), TestCache.Silent(), NullLogger<ProductMaintenance>.Instance);

    private sealed class ContextFactory(string connectionString) : IDbContextFactory<CatalogDbContext>
    {
        public CatalogDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connectionString).Options);
    }
}
