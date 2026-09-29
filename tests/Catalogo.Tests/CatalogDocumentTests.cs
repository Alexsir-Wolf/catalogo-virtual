using Catalogo.Features.CatalogBuilder;
using Catalogo.Features.PdfExport;
using Catalogo.Features.Products;
using PdfSharp.Pdf.IO;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Catalogo.Tests;

/// <summary>
/// Composição das páginas de conteúdo (T-24, UI-09). Não toca banco nem armazenamento: o
/// recorte já vem resolvido de T-23, e o que se verifica aqui é o documento.
///
/// **Como estes casos verificam, e por quê.** O PDF gerado embute a fonte em subconjunto e
/// codifica o texto por identificador de glifo — sondagem confirmou que o conteúdo
/// descomprimido não contém `TINTAS` nem `pág. 2`, e portanto extração de texto do arquivo não
/// prova nada. As regras que produzem texto foram expostas como membros nomeados e são
/// verificadas ali; do arquivo em si verifica-se o que ele realmente permite afirmar, que é a
/// paginação.
///
/// **O que nenhum destes casos cobre:** fidelidade visual ao gabarito. É julgamento humano,
/// está declarado como ponto de validação no plano, e segue aberto.
/// </summary>
public sealed class CatalogDocumentTests
{
    public CatalogDocumentTests() =>
        QuestPDF.Settings.License = LicenseType.Community;

    /// <summary>
    /// CA-10 e RN-39: o título da seção usa o número **do recorte**, não o do cadastro.
    /// </summary>
    [Fact]
    public void CA_10_o_titulo_da_secao_usa_a_numeracao_do_recorte()
    {
        Assert.Equal("01 TINTAS", CatalogDocument.SectionTitleFor(Category("Tintas", number: 1)));
        Assert.Equal("02 REDES", CatalogDocument.SectionTitleFor(Category("Redes", number: 2)));
    }

    [Fact]
    public void RN_07_o_rotulo_impresso_distingue_preco_de_preco_por_unidade()
    {
        Assert.Equal("PREÇO", CatalogDocument.LabelOf(PriceLabel.Price));
        Assert.Equal("PREÇO/UND", CatalogDocument.LabelOf(PriceLabel.PricePerUnit));
    }

    /// <summary>
    /// RN-40.1: a numeração do rodapé **conta a capa**. Começar em 1 nas páginas de conteúdo
    /// produziria um documento cujo rodapé não bate com a folha que a pessoa tem na mão.
    /// </summary>
    [Fact]
    public void RN_40_1_a_numeracao_do_rodape_conta_a_capa()
    {
        var document = Document(Catalog(Category("Única", number: 1, Product("Item"))));

        Assert.Equal(2, document.FolioFor(1));
        Assert.Equal(3, document.FolioFor(2));
    }

    /// <summary>
    /// RN-41, a grade de três colunas: sete produtos ocupam três linhas — duas cheias e uma com
    /// um item, que é a "última linha incompleta" que o gabarito mostra em quase toda categoria.
    /// </summary>
    [Fact]
    public void RN_41_a_grade_agrupa_de_tres_em_tres_e_admite_linha_incompleta()
    {
        var products = Enumerable.Range(1, 7).Select(index => Product($"Produto {index}")).ToList();

        var lines = CatalogDocument.Lines(products).ToList();

        Assert.Equal(3, lines.Count);
        Assert.Equal(3, lines[0].Count);
        Assert.Equal(3, lines[1].Count);
        Assert.Single(lines[2]);
    }

    /// <summary>
    /// A grade não reordena nada: a ordem é decisão de T-23 (RN-40), e duplicá-la aqui criaria
    /// uma segunda fonte de verdade para a regra.
    /// </summary>
    [Fact]
    public void A_grade_preserva_a_ordem_recebida()
    {
        var products = new[] { Product("Primeiro"), Product("Segundo"), Product("Terceiro") };

        var line = CatalogDocument.Lines(products).Single();

        Assert.Equal(["Primeiro", "Segundo", "Terceiro"], line.Select(product => product.Name));
    }

    /// <summary>
    /// CA-04: a célula de produto sem resumo é composta sem o bloco de texto, e o documento sai
    /// íntegro. O que não pode acontecer é a composição falhar ou a grade desalinhar por causa
    /// da ausência — nada é herdado da descrição, que por decisão da ADR-016 não entra no PDF.
    /// </summary>
    [Fact]
    public void CA_04_produto_sem_resumo_compoe_sem_quebrar_a_grade()
    {
        var pdf = Compose(Catalog(Category(
            "Cabos",
            number: 1,
            Product("Sem resumo", summary: null),
            Product("Com resumo", summary: "Dois metros, blindado"),
            Product("Resumo vazio", summary: "   "))));

        Assert.Equal(1, PageCountOf(pdf));
    }

    /// <summary>
    /// RN-41: sete produtos cabem em uma página. Se a grade estivesse agrupando errado — um por
    /// linha, por exemplo — o documento passaria de uma página.
    /// </summary>
    [Fact]
    public void RN_41_sete_produtos_cabem_em_uma_pagina()
    {
        var products = Enumerable.Range(1, 7).Select(index => Product($"Produto {index}")).ToArray();

        var pdf = Compose(Catalog(Category("Grade", number: 1, products)));

        Assert.Equal(1, PageCountOf(pdf));
    }

    /// <summary>
    /// CA-30 / RN-42: com produtos suficientes para atravessar páginas, o documento sai íntegro
    /// e com mais de uma folha. A garantia de que nenhuma célula é partida está no `ShowEntire`,
    /// e o que este caso prova é que a composição sobrevive à quebra — uma célula que não caiba
    /// e não possa descer faria a composição falhar em vez de gerar.
    /// </summary>
    [Fact]
    public void CA_30_documento_atravessa_paginas_sem_quebrar_a_composicao()
    {
        var products = Enumerable.Range(1, 60)
            .Select(index => Product($"Produto {index}", summary: $"Resumo do item {index}"))
            .ToArray();

        var pdf = Compose(Catalog(Category("Volume", number: 1, products)));

        Assert.True(PageCountOf(pdf) > 1);
    }

    /// <summary>
    /// Várias categorias fluem no mesmo documento sem quebra forçada entre elas (RN-41).
    /// </summary>
    [Fact]
    public void RN_41_categorias_fluem_sem_quebra_de_pagina_forcada()
    {
        var pdf = Compose(Catalog(
            Category("Alfa", number: 1, Product("A")),
            Category("Beta", number: 2, Product("B")),
            Category("Gama", number: 3, Product("C"))));

        // Três categorias com um produto cada cabem na mesma folha: se cada categoria forçasse
        // quebra, sairiam três.
        Assert.Equal(1, PageCountOf(pdf));
    }

    /// <summary>
    /// RN-67: o rodapé das páginas de conteúdo carrega os dados de contato das Configurações —
    /// a **mesma origem** que alimenta a vitrine. É a metade do CA-38 que T-31 não podia fechar.
    /// </summary>
    [Fact]
    public void RN_67_o_rodape_carrega_os_contatos_das_configuracoes()
    {
        var line = new DocumentFooter(
            "(88) 99654-1931",
            "5588996541931",
            "vendas@exemplo.com.br").Line;

        Assert.Contains("(88) 99654-1931", line);
        Assert.Contains("5588996541931", line);
        Assert.Contains("vendas@exemplo.com.br", line);
    }

    /// <summary>
    /// Sem contato configurado o rodapé sai com o nome da empresa e nada mais — não com
    /// separadores soltos onde deveria haver telefone.
    /// </summary>
    [Fact]
    public void Sem_contato_o_rodape_nao_sai_com_separadores_soltos()
    {
        var line = new DocumentFooter(null, null, null).Line;

        Assert.Equal(DocumentFooter.CompanyName, line);
        Assert.DoesNotContain("·", line);
    }

    [Fact]
    public void O_rodape_omite_apenas_o_canal_ausente()
    {
        var line = new DocumentFooter(null, null, "vendas@exemplo.com.br").Line;

        Assert.Contains("vendas@exemplo.com.br", line);
        Assert.DoesNotContain("WhatsApp", line);
    }

    /// <summary>
    /// Produto sem foto não impede a composição: o espaço é reservado para a grade não
    /// desalinhar. Produto sem foto é lacuna declarada da SPEC-UI, e a decisão aqui é não
    /// derrubar o documento por causa dela.
    /// </summary>
    [Fact]
    public void Produto_sem_foto_nao_impede_a_composicao()
    {
        var pdf = Compose(Catalog(Category("Sem foto", number: 1, Product("Item sem imagem"))));

        Assert.Equal(1, PageCountOf(pdf));
    }

    private static byte[] Compose(ResolvedCatalog catalog) => Document(catalog).GeneratePdf();

    private static CatalogDocument Document(ResolvedCatalog catalog) =>
        new(
            catalog,
            new DocumentFooter("(88) 99654-1931", null, null),

            // Sem imagem: baixar as derivadas é papel do compositor, não do documento.
            _ => null);

    private static int PageCountOf(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf, writable: false);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

        return document.PageCount;
    }

    private static ResolvedCatalog Catalog(params ResolvedCategory[] categories) =>
        new(1, "Catálogo de teste", categories, LastGeneratedAt: null);

    private static ResolvedCategory Category(
        string name,
        int number,
        params ResolvedProduct[] products) =>
        new(number, name, number, products);

    private static ResolvedProduct Product(
        string name,
        string? summary = "Resumo padrão",
        PriceLabel label = PriceLabel.Price) =>
        new(1, name, summary, 149.90m, label, null, null, IsNewSinceLastGeneration: false);
}
