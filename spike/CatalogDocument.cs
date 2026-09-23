using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Spike;

/// <summary>
/// Reproduz em código as páginas de conteúdo do gabarito (ADR-012): grade de três colunas,
/// categorias em fluxo contínuo, cabeçalho e rodapé repetidos com numeração.
/// A capa não entra aqui — é arquivo enviado pelo dono e concatenado depois (ADR-017).
/// </summary>
public sealed class CatalogDocument : IDocument
{
    private const int ColumnCount = 3;
    private const float PagePadding = 14f;
    private const float CellSpacing = 8f;
    private const string CompanyFooter = "SIG · Suprimentos & Informática · Responsável – (35) 0000-0000";

    private readonly IReadOnlyList<Category> categories;

    public CatalogDocument(IReadOnlyList<Category> categories) => this.categories = categories;

    public void Compose(IDocumentContainer container) =>
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(PagePadding, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(9));

            page.Header().AlignRight().Text("CATÁLOGO DE PRODUTOS")
                .FontSize(10).Bold().LetterSpacing(0.08f);

            page.Content().PaddingVertical(6).Column(ComposeCategories);

            page.Footer().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text(CompanyFooter).FontSize(7).FontColor(Colors.Grey.Darken1);
                row.ConstantItem(60).AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(7).FontColor(Colors.Grey.Darken1));
                    text.Span("pág. ");
                    text.CurrentPageNumber();
                });
            });
        });

    private void ComposeCategories(ColumnDescriptor column)
    {
        var position = 1;

        foreach (var category in categories)
        {
            column.Item().PaddingTop(10).PaddingBottom(4).Text($"{position:D2} {category.Name.ToUpperInvariant()}")
                .FontSize(12).Bold().LetterSpacing(0.05f);

            foreach (var row in Chunk(category.Products))
            {
                column.Item().PaddingBottom(CellSpacing).Row(grid =>
                {
                    foreach (var product in row)
                    {
                        grid.RelativeItem().PaddingRight(CellSpacing).Element(cell => ComposeProduct(cell, product));
                    }

                    for (var empty = row.Count; empty < ColumnCount; empty++)
                    {
                        grid.RelativeItem();
                    }
                });
            }

            position++;
        }
    }

    private static void ComposeProduct(IContainer container, Product product) =>
        // ShowEntire é o que impede a célula de partir entre páginas — exigência do gabarito.
        container.ShowEntire().Column(cell =>
        {
            cell.Item().Height(90).AlignCenter().Image(product.PhotoPath).FitArea();

            cell.Item().PaddingTop(5).Text(product.Name).FontSize(8.5f).Bold();

            if (!string.IsNullOrEmpty(product.Description))
            {
                cell.Item().PaddingTop(1).Text(product.Description)
                    .FontSize(7.5f).FontColor(Colors.Grey.Darken2);
            }

            cell.Item().PaddingTop(4).Text(text =>
            {
                text.Span($"{product.PriceLabel} ").FontSize(7).FontColor(Colors.Grey.Darken1);
                text.Span(product.Price.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")))
                    .FontSize(10).Bold();
            });
        });

    private static IEnumerable<IReadOnlyList<Product>> Chunk(IReadOnlyList<Product> products)
    {
        for (var start = 0; start < products.Count; start += ColumnCount)
        {
            yield return products.Skip(start).Take(ColumnCount).ToList();
        }
    }
}
