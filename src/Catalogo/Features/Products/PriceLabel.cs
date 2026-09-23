namespace Catalogo.Features.Products;

/// <summary>
/// Rótulo impresso acima do valor no PDF e exibido na página de detalhe (RN-07).
/// Lista fechada — o padrão é <see cref="Price"/>.
/// </summary>
public enum PriceLabel
{
    Price = 0,
    PricePerUnit = 1
}
