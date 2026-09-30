using Catalogo.Data;
using Catalogo.Features.Categories;
using Catalogo.Features.Storefront;
using Microsoft.EntityFrameworkCore;

namespace Catalogo.Features.Products;

/// <summary>
/// Reposicionamento manual do produto dentro da sua categoria (RN-21). A ordem é uma só:
/// a posição definida aqui vale ao mesmo tempo para a ordenação da vitrine e para a
/// sequência impressa no PDF, e nenhum catálogo define ordem própria (RN-22, ADR-015).
/// Mover um produto, portanto, move em todos os recortes onde ele aparece.
/// </summary>
public sealed class ProductOrdering(
    IDbContextFactory<CatalogDbContext> contextFactory,
    StorefrontInvalidation cache)
{
    /// <summary>
    /// Troca o produto de lugar com o vizinho da mesma categoria na direção pedida.
    /// Mover o primeiro para cima ou o último para baixo não faz nada — os extremos são
    /// limite, não erro, e quem chama já os apresenta como ação indisponível.
    /// </summary>
    public async Task MoveAsync(
        int productId,
        MoveDirection direction,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var categoryId = await context.Products
            .Where(product => product.Id == productId)
            .Select(product => (int?)product.CategoryId)
            .SingleOrDefaultAsync(cancellationToken);

        if (categoryId is null)
        {
            return;
        }

        // O vizinho é o produto seguinte da mesma categoria, então a lista carregada é a
        // categoria inteira: a posição é relativa a ela, não ao acervo.
        var ordered = await context.Products
            .Where(product => product.CategoryId == categoryId)
            .InCuratedOrder()
            .ToListAsync(cancellationToken);

        // A consulta anterior prova que o produto existia, não que ele ainda está na
        // lista: entre as duas, uma exclusão concorrente o tira. Sem esta guarda, um
        // índice de -1 escapa pela direção "para baixo" e alcança o indexador.
        var index = ordered.FindIndex(product => product.Id == productId);
        if (index < 0)
        {
            return;
        }

        var target = direction == MoveDirection.Up ? index - 1 : index + 1;
        if (target < 0 || target >= ordered.Count)
        {
            return;
        }

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);

        // Renumerar a categoria inteira para 1..N, e não apenas os dois envolvidos, é a
        // estratégia que T-10 adotou para as categorias. Ela corrige de passagem as
        // lacunas e os empates herdados, e mantém a numeração exibida sempre coerente
        // com a ordem persistida (ADR-015).
        for (var position = 0; position < ordered.Count; position++)
        {
            ordered[position].Position = position + 1;
        }

        await context.SaveChangesAsync(cancellationToken);

        // A ordem impressa e a da vitrine são a mesma (RN-21, RN-22): reordenar muda a página
        // pública. É o esquecimento típico que o plano de T-21 aponta.
        await cache.InvalidateAsync("ordem de produto alterada", cancellationToken);
    }
}
