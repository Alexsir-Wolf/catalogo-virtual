using System.Net;
using System.Text;
using Catalogo.Features.Media;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Xunit;

namespace Catalogo.Tests;

/// <summary>
/// CA-28 é regra de segurança e depende da política real dos buckets (ADR-018), então
/// estes testes batem no armazenamento de verdade — nunca em simulação. Sem credencial
/// configurada no ambiente, eles são pulados em vez de passar sem provar nada.
/// </summary>
public sealed class ObjectStorageTests
{
    private const string UrlVariable = "Supabase__Url";
    private const string ServiceKeyVariable = "Supabase__ServiceKey";

    [SkippableFact]
    public async Task Derivadas_de_tela_sao_acessiveis_por_url_publica()
    {
        var options = ReadOptionsOrSkip();
        var service = CreateService(options);

        using var image = CreateImage(1200, 900);
        var result = await service.StoreAsync(image, image.Length);
        Assert.True(result.Succeeded);

        using var anonymous = new HttpClient();
        var response = await anonymous.GetAsync(service.PublicUrlFor(result.Photo!.CardFileName));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [SkippableFact]
    public async Task CA_28_derivada_de_impressao_nao_e_acessivel_sem_credencial()
    {
        var options = ReadOptionsOrSkip();
        var service = CreateService(options);

        using var image = CreateImage(1200, 900);
        var result = await service.StoreAsync(image, image.Length);
        Assert.True(result.Succeeded);

        var privateUrl =
            $"{options.Url.TrimEnd('/')}/storage/v1/object/public/{options.PrivateBucket}/{result.Photo!.PrintFileName}";

        using var anonymous = new HttpClient();
        var response = await anonymous.GetAsync(privateUrl);

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden
                or HttpStatusCode.Unauthorized,
            $"A derivada de impressão respondeu {(int)response.StatusCode} a uma requisição anônima.");
    }

    private static ObjectStorageOptions ReadOptionsOrSkip()
    {
        var url = Environment.GetEnvironmentVariable(UrlVariable);
        var serviceKey = Environment.GetEnvironmentVariable(ServiceKeyVariable);

        Skip.If(
            string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(serviceKey),
            $"Defina {UrlVariable} e {ServiceKeyVariable} para exercer o armazenamento real.");

        return new ObjectStorageOptions { Url = url!, ServiceKey = serviceKey! };
    }

    private static ProductPhotoService CreateService(ObjectStorageOptions options)
    {
        var wrapped = Options.Create(options);
        var storage = new SupabaseObjectStorage(new HttpClient(), wrapped);

        return new ProductPhotoService(storage, new ImageProcessor(), wrapped);
    }

    private static MemoryStream CreateImage(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.SeaGreen);

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        return new MemoryStream(encoded.ToArray());
    }
}
