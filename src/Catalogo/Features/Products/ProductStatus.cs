namespace Catalogo.Features.Products;

/// <summary>
/// Situação do produto. Todo produto nasce em <see cref="Draft"/> (RN-14) e só
/// aparece na vitrine e nos catálogos quando está em <see cref="Published"/> (RN-15).
/// </summary>
public enum ProductStatus
{
    Draft = 0,
    Published = 1
}
