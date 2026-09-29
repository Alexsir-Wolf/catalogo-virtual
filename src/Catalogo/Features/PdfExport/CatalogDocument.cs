using System.Globalization;
using Catalogo.Features.CatalogBuilder;
using Catalogo.Features.Products;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Catalogo.Features.PdfExport;

/// <summary>Dados de contato como o rodapé do documento os consome (RN-67).</summary>
public sealed record DocumentFooter(string? Phone, string? WhatsApp, string? Email)
{
    /// <summary>
    /// A linha que sai impressa. Vem das Configurações, e é **a mesma origem** que alimenta a
    /// vitrine — a RN-67 existe para os dois não divergirem.
    /// </summary>
    public string Line
    {
        get
        {
            var channels = new[] { Phone, WhatsApp is { } number ? $"WhatsApp {number}" : null, Email }
                .Where(channel => !string.IsNullOrWhiteSpace(channel));

            var contacts = string.Join(" · ", channels);

            return contacts.Length == 0 ? CompanyName : $"{CompanyName} · {contacts}";
        }
    }

    public const string CompanyName = "Suprimentos & Informática";
}

/// <summary>
/// As **páginas de conteúdo** do catálogo (UI-09), compostas a partir do recorte resolvido.
///
/// A capa não está aqui e não deve estar: ela é um arquivo que o dono envia, concatenado ao
/// miolo em T-32 (ADR-017). **Não há página de índice** — a RN-38 foi revogada.
///
/// O layout vem do gabarito do cliente, analisado em `docs/prototype/referencia-layout-pdf.md`
/// e reproduzido pelo spike de T-03, que é a origem das medidas usadas aqui. **As medidas
/// exatas de tipografia continuam sendo lacuna declarada** (lacuna 3 da SPEC-UI, ponto de
/// validação humana "antes de T-24"): o que existe é a reprodução que o spike validou como
/// suficiente, não uma extração do arquivo original.
/// </summary>
public sealed class CatalogDocument(
    ResolvedCatalog catalog,
    DocumentFooter footer,
    Func<string, byte[]?> printImage,
    int coverPages = CatalogDocument.DefaultCoverPages) : IDocument
{
    /// <summary>
    /// Quantas folhas a capa ocupa. A numeração do rodapé conta a capa (RN-40.1): começar em 1
    /// nas páginas de conteúdo produziria um documento cujo rodapé não bate com a folha que a
    /// pessoa tem na mão.
    /// </summary>
    public const int DefaultCoverPages = 1;

    private const int ColumnCount = 3;
    private const float PageMarginMillimetres = 14f;
    private const float CellSpacing = 8f;
    private const float PhotoHeight = 90f;

    private static readonly CultureInfo Brazil = CultureInfo.GetCultureInfo("pt-BR");

    public void Compose(IDocumentContainer container) =>
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(PageMarginMillimetres, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(9));

            page.Header().AlignRight().Text("CATÁLOGO DE PRODUTOS")
                .FontSize(10).Bold().LetterSpacing(0.08f);

            page.Content().PaddingVertical(6).Column(ComposeCategories);

            page.Footer().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text(footer.Line)
                    .FontSize(7).FontColor(Colors.Grey.Darken1);

                row.ConstantItem(70).AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(7).FontColor(Colors.Grey.Darken1));
                    text.Span("pág. ");

                    // A capa entra na conta: a primeira página de conteúdo é a folha 2 quando a
                    // capa tem uma página (RN-40.1).
                    text.CurrentPageNumber().Format(number => FolioFor(number ?? 1).ToString());
                });
            });
        });

    private void ComposeCategories(ColumnDescriptor column)
    {
        foreach (var category in catalog.Categories)
        {
            // O número é o do recorte, já calculado na resolução (RN-39). Recalcular aqui seria
            // duplicar a regra mais fácil de errar do sistema.
            column.Item().PaddingTop(10).PaddingBottom(4)
                .Text(SectionTitleFor(category))
                .FontSize(12).Bold().LetterSpacing(0.05f);

            // As categorias fluem continuamente, sem quebra de página forçada (RN-41): a última
            // linha de uma categoria pode ficar incompleta e a seguinte começa na linha de
            // baixo, como no gabarito.
            foreach (var line in Lines(category.Products))
            {
                column.Item().PaddingBottom(CellSpacing).Row(grid =>
                {
                    foreach (var product in line)
                    {
                        grid.RelativeItem().PaddingRight(CellSpacing)
                            .Element(cell => ComposeProduct(cell, product));
                    }

                    // As colunas vazias da última linha precisam existir para as células
                    // ocupadas não esticarem até a largura da página.
                    for (var empty = line.Count; empty < ColumnCount; empty++)
                    {
                        grid.RelativeItem();
                    }
                });
            }
        }
    }

    private void ComposeProduct(IContainer container, ResolvedProduct product) =>
        // `ShowEntire` é o que impede a célula de partir entre páginas (RN-42, CA-30): quando o
        // bloco não cabe no resto da folha, ele desce inteiro.
        container.ShowEntire().Column(cell =>
        {
            // **A derivada de impressão, nunca a de tela.** Usar a miniatura aqui produz página
            // borrada, e o erro só aparece no papel.
            if (product.PrintFileName is { } name && printImage(name) is { } image)
            {
                cell.Item().Height(PhotoHeight).AlignCenter().Image(image).FitArea();
            }
            else
            {
                // Produto sem foto é lacuna declarada da SPEC-UI (a grade de três colunas do
                // gabarito depende da imagem). O espaço é reservado para a grade não desalinhar.
                cell.Item().Height(PhotoHeight);
            }

            cell.Item().PaddingTop(5).Text(product.Name).FontSize(8.5f).Bold();

            // CA-04: sem resumo, a célula mostra **apenas nome e preço** — nada é herdado da
            // descrição, que por decisão da ADR-016 não entra no documento.
            if (!string.IsNullOrWhiteSpace(product.Summary))
            {
                cell.Item().PaddingTop(1).Text(product.Summary)
                    .FontSize(7.5f).FontColor(Colors.Grey.Darken2);
            }

            cell.Item().PaddingTop(4).Text(text =>
            {
                // RN-07: o rótulo fica acima do valor, não embutido nele.
                text.Span($"{LabelOf(product.PriceLabel)} ")
                    .FontSize(7).FontColor(Colors.Grey.Darken1);

                text.Span(product.Price.ToString("C2", Brazil)).FontSize(10).Bold();
            });
        });

    /// <summary>
    /// O rótulo impresso acima do valor (RN-07).
    /// </summary>
    public static string LabelOf(PriceLabel label) => label switch
    {
        PriceLabel.PricePerUnit => "PREÇO/UND",
        _ => "PREÇO"
    };

    /// <summary>
    /// O título da seção, como sai no papel: número **do recorte** e nome em caixa alta
    /// (RN-39). É membro nomeado, e não texto solto no meio do layout, porque é regra —
    /// o PDF gerado tem fonte subsetada e não permite verificar isso por extração de texto.
    /// </summary>
    public static string SectionTitleFor(ResolvedCategory category) =>
        $"{category.Label} {category.Name.ToUpperInvariant()}";

    /// <summary>
    /// A folha que o rodapé imprime para uma página de conteúdo: a capa entra na conta
    /// (RN-40.1).
    /// </summary>
    public int FolioFor(int contentPageNumber) => contentPageNumber + coverPages;

    /// <summary>
    /// Os produtos agrupados em linhas da grade de três colunas (RN-41). A última linha de
    /// cada categoria pode ficar incompleta, e a categoria seguinte começa na linha de baixo.
    /// </summary>
    public static IEnumerable<IReadOnlyList<ResolvedProduct>> Lines(
        IReadOnlyList<ResolvedProduct> products)
    {
        for (var start = 0; start < products.Count; start += ColumnCount)
        {
            yield return [.. products.Skip(start).Take(ColumnCount)];
        }
    }
}
