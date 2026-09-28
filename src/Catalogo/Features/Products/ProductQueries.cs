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
}
