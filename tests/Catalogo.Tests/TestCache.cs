using Catalogo.Features.Storefront;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catalogo.Tests;

/// <summary>
/// Invalidação do cache da vitrine para os testes que constroem serviços à mão.
///
/// Existe como peça única porque **todo** serviço de escrita do painel depende dela desde T-21 —
/// é o único acoplamento entre painel e vitrine (ADR-008). Repetir a construção em cada arquivo
/// de teste faria o próximo serviço de escrita nascer com o dobro de atrito.
/// </summary>
public static class TestCache
{
    /// <summary>Invalidação que registra as tags evictadas, para os testes de T-21 as afirmarem.</summary>
    public static (StorefrontInvalidation Invalidation, RecordingOutputCacheStore Store) Recording()
    {
        var store = new RecordingOutputCacheStore();

        return (new StorefrontInvalidation(store, NullLogger<StorefrontInvalidation>.Instance), store);
    }

    /// <summary>Invalidação silenciosa, para os testes que não são sobre cache.</summary>
    public static StorefrontInvalidation Silent() => Recording().Invalidation;
}

public sealed class RecordingOutputCacheStore : IOutputCacheStore
{
    private readonly List<string> evicted = [];

    public IReadOnlyList<string> Evicted => evicted;

    public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken)
    {
        evicted.Add(tag);

        return ValueTask.CompletedTask;
    }

    public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken) =>
        ValueTask.FromResult<byte[]?>(null);

    public ValueTask SetAsync(
        string key,
        byte[] value,
        string[]? tags,
        TimeSpan validFor,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
