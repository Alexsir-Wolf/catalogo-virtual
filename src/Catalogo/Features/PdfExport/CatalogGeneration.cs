using Catalogo.Features.CatalogBuilder;
using Catalogo.Features.Settings;

namespace Catalogo.Features.PdfExport;

public enum GenerationRefusal
{
    None,

    /// <summary>O catálogo não existe mais — excluído noutra aba.</summary>
    NotFound,

    /// <summary>Nenhum produto No ar atende ao critério (RN-46).</summary>
    NoProducts,

    /// <summary>Acima do teto de produtos (RN-45).</summary>
    AboveLimit,

    /// <summary>Sem capa configurada nas Configurações (RN-65).</summary>
    NoCover,

    /// <summary>Já existe uma geração em curso (RN-44: uma de cada vez).</summary>
    AlreadyRunning,

    /// <summary>Falha durante a composição.</summary>
    Failed
}

/// <summary>
/// O resultado da geração. <see cref="Content"/> só existe quando deu certo — **nada parcial
/// é entregue**: um PDF truncado parece um arquivo válido até alguém tentar imprimi-lo.
/// </summary>
public sealed record GenerationOutcome(
    GenerationRefusal Refusal,
    byte[]? Content = null,
    string? FileName = null,
    int ProductCount = 0,

    /// <summary>
    /// O instante do conteúdo, capturado **antes** de resolver o critério. É o que vai para o
    /// registro da RN-33 quando a entrega se confirmar.
    /// </summary>
    DateTimeOffset? Moment = null)
{
    public bool Succeeded => Refusal == GenerationRefusal.None;

    public static GenerationOutcome Generated(
        byte[] content,
        string fileName,
        int productCount,
        DateTimeOffset moment) =>
        new(GenerationRefusal.None, content, fileName, productCount, moment);

    public static GenerationOutcome Refused(GenerationRefusal refusal, int productCount = 0) =>
        new(refusal, ProductCount: productCount);
}

/// <summary>
/// Geração do documento (RN-35, RN-44, RN-45).
///
/// **O arquivo não é gravado em disco em momento nenhum.** Ele nasce em memória, é entregue
/// como download e descartado (RN-35, ADR-014). Não há pasta de saída, nome de arquivo no
/// servidor nem limpeza a fazer — a ausência de armazenamento é o que torna impossível servir
/// uma versão velha por engano, e é também por isso que a tela avisa ao dono que **aquele
/// arquivo é o único registro daquele envio**.
///
/// **As recusas acontecem todas antes de compor**, e essa ordem é a decisão: compor primeiro e
/// recusar depois gastaria memória e CPU do mesmo processo que serve a vitrine (ADR-008,
/// ADR-013) para jogar o resultado fora.
/// </summary>
public sealed class CatalogGeneration(
    CatalogResolution resolution,
    CatalogComposer composer,
    CatalogMaintenance maintenance,
    PortalSettingsService settings,
    ICoverSource covers,
    TimeProvider time,
    ILogger<CatalogGeneration> logger)
{
    /// <summary>
    /// Teto de produtos por catálogo (RN-45), fixado em 250 pela ADR-013 a partir de T-04.
    ///
    /// Não é limitação arbitrária: é o que impede uma seleção acidental de "todas as
    /// categorias" de degradar a vitrine de quem está navegando, porque a geração compete por
    /// CPU e memória no mesmo processo. O valor é conservador de propósito — o spike mediu
    /// 18 ms por produto em máquina de desenvolvimento, e a plataforma gratuita é mais lenta.
    /// </summary>
    public const int MaxProducts = 250;

    /// <summary>
    /// Uma geração de cada vez (RN-44). Não é otimização: duas composições simultâneas
    /// dobrariam o pico de memória do processo que também serve a vitrine.
    /// </summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<GenerationOutcome> GenerateAsync(
        int catalogId,
        IProgress<GenerationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!await Gate.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            return GenerationOutcome.Refused(GenerationRefusal.AlreadyRunning);
        }

        try
        {
            return await GenerateOnceAsync(catalogId, progress, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<GenerationOutcome> GenerateOnceAsync(
        int catalogId,
        IProgress<GenerationProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(GenerationProgress.Resolving);

        // O instante é capturado **antes** de resolver, e é ele que vai para o registro da
        // geração. Marcar com a hora do fim da composição abriria uma janela de segundos a
        // minutos — baixar até 250 imagens e concatenar leva tempo — em que um produto publicado
        // **não sai no PDF entregue** e ainda assim fica com `PublishedAt` anterior à data
        // gravada: nunca mais seria destacado em prévia alguma (RN-32, RN-33).
        var moment = time.GetUtcNow();

        var resolved = await resolution.ResolveAsync(catalogId, cancellationToken);

        if (resolved is null)
        {
            return GenerationOutcome.Refused(GenerationRefusal.NotFound);
        }

        // RN-46: sem produto No ar não há documento.
        if (resolved.IsEmpty)
        {
            return GenerationOutcome.Refused(GenerationRefusal.NoProducts);
        }

        // RN-45, **antes** de compor: é o ponto do teto.
        if (resolved.ProductCount > MaxProducts)
        {
            logger.LogInformation(
                "Geração do catálogo {Id} recusada: {Resolvidos} produtos, teto de {Teto}.",
                catalogId,
                resolved.ProductCount,
                MaxProducts);

            return GenerationOutcome.Refused(GenerationRefusal.AboveLimit, resolved.ProductCount);
        }

        // RN-65: sem capa a geração é recusada. A capa é concatenada em T-32, e compor o miolo
        // para descobrir depois que falta a capa seria trabalho jogado fora.
        var portal = await settings.LoadAsync(cancellationToken);

        if (!portal.HasCover)
        {
            return GenerationOutcome.Refused(GenerationRefusal.NoCover, resolved.ProductCount);
        }

        progress?.Report(GenerationProgress.Composing(resolved.ProductCount));

        byte[] content;

        try
        {
            content = await composer.ComposeAsync(resolved, cancellationToken);

            progress?.Report(GenerationProgress.Merging);

            // RN-36: o documento é a capa do dono **mais** as páginas compostas. A concatenação
            // acontece aqui, e não dentro da composição, porque a capa não é conteúdo que o
            // sistema desenha — é arquivo que ele carrega inteiro (RN-37, ADR-017).
            content = CoverMerge.Merge(
                await covers.DownloadAsync(portal.CoverFileName!, cancellationToken),
                content);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Nada parcial é entregue: o resultado não traz conteúdo, e a tela oferece repetir.
            logger.LogError(exception, "Falha ao compor o catálogo {Id}.", catalogId);

            return GenerationOutcome.Refused(GenerationRefusal.Failed, resolved.ProductCount);
        }

        progress?.Report(GenerationProgress.Done);

        // **A data não é gravada aqui.** "O documento existe" não é "o documento chegou": a
        // entrega acontece depois, pelo circuito, e pode falhar — reconexão, aba fechada, tempo
        // de espera do JavaScript esgotado. Gravar antes fazia `LastGeneratedAt` avançar para uma
        // geração que ninguém recebeu, e o destaque da RN-32 passava a comparar com ela: os
        // produtos que entraram desde a geração **anterior** deixavam de ser destacados para
        // sempre. Quem confirma a entrega é quem entrega — ver `ConfirmDeliveryAsync`.
        return GenerationOutcome.Generated(
            content,
            FileNameFor(resolved),
            resolved.ProductCount,
            moment);
    }

    /// <summary>
    /// Registra a geração (RN-33), e só depois de a entrega ter acontecido.
    ///
    /// Fica separado de <see cref="GenerateAsync"/> porque a entrega é do circuito, não daqui: o
    /// documento pode existir e não chegar. Enquanto esta chamada não acontece, o catálogo segue
    /// com a data anterior — e o destaque da RN-32 continua comparando com a última geração que
    /// alguém de fato recebeu.
    /// </summary>
    public Task ConfirmDeliveryAsync(
        int catalogId,
        GenerationOutcome outcome,
        CancellationToken cancellationToken = default) =>
        outcome is { Succeeded: true, Moment: { } moment }
            ? maintenance.MarkGeneratedAsync(catalogId, moment, cancellationToken)
            : Task.CompletedTask;

    /// <summary>
    /// Nome do arquivo que o dono recebe. Leva o nome do catálogo e a data, porque o arquivo
    /// **é o único registro daquele envio** — o sistema não guarda cópia (RN-35), e um nome
    /// genérico faria dois envios diferentes se confundirem na pasta de downloads.
    /// </summary>
    public string FileNameFor(ResolvedCatalog catalog)
    {
        var slug = new string([.. catalog.Name
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')])
            .Trim('-');

        while (slug.Contains("--"))
        {
            slug = slug.Replace("--", "-");
        }

        var stamp = time.GetLocalNow().ToString("yyyy-MM-dd");

        return $"catalogo-{(slug.Length == 0 ? "sem-nome" : slug)}-{stamp}.pdf";
    }
}

/// <summary>
/// Etapa corrente da geração, para o progresso da UI-08 (RN-44).
/// </summary>
public sealed record GenerationProgress(string Stage, int ProductCount = 0)
{
    public static readonly GenerationProgress Resolving =
        new("Resolvendo o critério e conferindo o acervo…");

    public static GenerationProgress Composing(int productCount) =>
        new($"Compondo {productCount} {(productCount == 1 ? "produto" : "produtos")}…", productCount);

    public static readonly GenerationProgress Merging =
        new("Unindo a capa às páginas de conteúdo…");

    public static readonly GenerationProgress Done = new("Documento pronto.");
}
