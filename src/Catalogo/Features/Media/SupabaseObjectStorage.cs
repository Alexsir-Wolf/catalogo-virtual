using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace Catalogo.Features.Media;

/// <summary>
/// Armazenamento de objeto no Supabase (ADR-018). A separação entre bucket público e
/// privado é o que sustenta o CA-28: a derivada de impressão vive em um bucket sem
/// leitura anônima, e nenhuma URL pública é emitida para ela.
/// </summary>
public sealed class SupabaseObjectStorage(
    HttpClient client,
    IOptions<ObjectStorageOptions> options) : IObjectStorage
{
    private const string CacheControlForImmutableObjects = "public, max-age=31536000, immutable";

    private readonly ObjectStorageOptions options = options.Value;

    public async Task UploadAsync(
        string bucket,
        string objectName,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ObjectPath(bucket, objectName))
        {
            Content = new ByteArrayContent(content)
        };

        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        request.Headers.TryAddWithoutValidation("cache-control", CacheControlForImmutableObjects);
        Authorize(request);

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(
        string bucket,
        string objectName,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, ObjectPath(bucket, objectName));
        Authorize(request);

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public string PublicUrlFor(string objectName) =>
        $"{options.Url.TrimEnd('/')}/storage/v1/object/public/{options.PublicBucket}/{objectName}";

    private string ObjectPath(string bucket, string objectName) =>
        $"{options.Url.TrimEnd('/')}/storage/v1/object/{bucket}/{objectName}";

    private void Authorize(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ServiceKey);
        request.Headers.TryAddWithoutValidation("apikey", options.ServiceKey);
    }
}
