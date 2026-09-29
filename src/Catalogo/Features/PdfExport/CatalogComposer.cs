using Catalogo.Features.CatalogBuilder;
using Catalogo.Features.Media;
using Catalogo.Features.Settings;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Catalogo.Features.PdfExport;

/// <summary>
/// Compõe as páginas de conteúdo de um catálogo resolvido (T-24).
///
/// As fotos são as **derivadas de impressão**, que vivem no bucket privado (RN-12): o
/// documento é montado no servidor e o arquivo nunca é exposto por URL pública, então a
/// derivada de impressão é lida daqui, não pela vitrine.
///
/// **A composição não decide o que entra.** O recorte já veio resolvido de T-23, e é a prévia
/// que o dono confirmou — compor a partir de outra consulta abriria a porta para o documento
/// divergir do que foi mostrado, que é exatamente o que a RN-31 existe para impedir.
/// </summary>
public sealed class CatalogComposer(
    IObjectStorage storage,
    IOptions<ObjectStorageOptions> storageOptions,
    PortalSettingsService settings,
    ILogger<CatalogComposer> logger)
{
    private readonly ObjectStorageOptions options = storageOptions.Value;

    public async Task<byte[]> ComposeAsync(
        ResolvedCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        if (catalog.IsEmpty)
        {
            // RN-46: sem produto No ar não há documento. Chegar aqui é erro de programação — a
            // tela impede antes —, e por isso é exceção e não resultado.
            throw new InvalidOperationException(
                "Catálogo sem produtos No ar não compõe documento (RN-46).");
        }

        var portal = await settings.LoadAsync(cancellationToken);
        var images = await LoadPrintImagesAsync(catalog, cancellationToken);

        var document = new CatalogDocument(
            catalog,
            new DocumentFooter(portal.Phone, portal.WhatsApp, portal.Email),
            name => images.GetValueOrDefault(name));

        // A composição é síncrona e com teto (ADR-013): o documento é montado inteiro em
        // memória e devolvido, sem arquivo intermediário — o disco do host é efêmero e o PDF
        // não é armazenado (RN-35, ADR-014).
        return document.GeneratePdf();
    }

    /// <summary>
    /// Baixa as derivadas de impressão uma vez cada, mesmo que o produto apareça mais de uma
    /// vez. Falha em uma imagem **não derruba o documento**: a célula sai com o espaço
    /// reservado, e o nome vai para o log — um catálogo sem uma foto é entregável, um catálogo
    /// que não sai não é.
    /// </summary>
    private async Task<Dictionary<string, byte[]>> LoadPrintImagesAsync(
        ResolvedCatalog catalog,
        CancellationToken cancellationToken)
    {
        var names = catalog.Categories
            .SelectMany(category => category.Products)
            .Select(product => product.PrintFileName)
            .OfType<string>()
            .Distinct()
            .ToList();

        var images = new Dictionary<string, byte[]>(names.Count);

        foreach (var name in names)
        {
            try
            {
                images[name] = await storage.DownloadAsync(
                    options.PrivateBucket,
                    name,
                    cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(
                    exception,
                    "Falha ao baixar a derivada de impressão {Nome}. A célula sai sem foto.",
                    name);
            }
        }

        return images;
    }
}
