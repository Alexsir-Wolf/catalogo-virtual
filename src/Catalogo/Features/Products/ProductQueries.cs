using System.Linq.Expressions;

namespace Catalogo.Features.Products;

/// <summary>
/// Ponto único do filtro de visibilidade pública (RN-15). Vitrine, página de detalhe,
/// busca e resolução de catálogo compõem a consulta a partir daqui, em vez de cada uma
/// repetir a comparação de situação — repetida, a regra passa a ter tantas versões
/// quantas consultas existirem, e basta uma esquecer o filtro para o Rascunho vazar.
/// </summary>
public static class ProductQueries
{
    /// <summary>Apenas produtos No ar. Rascunho não aparece na vitrine nem entra em
    /// catálogo, mesmo atendendo ao critério da categoria (RN-15, RN-30).</summary>
    public static IQueryable<Product> Published(this IQueryable<Product> products) =>
        products.Where(product => product.Status == ProductStatus.Published);

    /// <summary>
    /// A ordem curada pelo dono, dentro da categoria. É uma só para os dois canais: a
    /// mesma sequência sai na vitrine e no PDF, porque a posição é atributo do produto e
    /// não do recorte (RN-21, RN-22, ADR-015). Um catálogo filtra; ele não reordena.
    /// </summary>
    public static IOrderedQueryable<Product> InCuratedOrder(this IQueryable<Product> products) =>
        products.OrderBy(ByPosition).ThenBy(ById);

    /// <summary>
    /// A mesma ordem, encadeada depois de um critério anterior. A listagem do painel e a
    /// da vitrine agrupam por categoria antes de chegar ao produto (RN-51, RN-24), e
    /// precisam da posição como critério secundário em vez de primário.
    /// </summary>
    public static IOrderedQueryable<Product> ThenInCuratedOrder(
        this IOrderedQueryable<Product> products) =>
        products.ThenBy(ByPosition).ThenBy(ById);

    private static readonly Expression<Func<Product, int>> ByPosition =
        product => product.Position;

    /// <summary>
    /// Desempate estável para posições iguais. Duas posições empatam hoje quando um
    /// produto troca de categoria sem recalcular a posição, e sem um segundo critério o
    /// banco fica livre para devolver ordens diferentes a cada requisição — a vitrine
    /// mostraria uma sequência e o PDF imprimiria outra. O id serve porque nunca muda;
    /// o nome, que as categorias usam, mudaria a ordem a cada renomeação.
    /// </summary>
    private static readonly Expression<Func<Product, int>> ById = product => product.Id;
}
