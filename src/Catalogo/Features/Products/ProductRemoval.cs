using Catalogo.Features.Media;

namespace Catalogo.Features.Products;

/// <summary>O que a exclusão fez, para a tela dizer a verdade ao dono.</summary>
public sealed record ProductRemovalOutcome(bool Removed, bool HadPhoto, string? Error = null)
{
    public bool Succeeded => Error is null;

    public static ProductRemovalOutcome Failed(string error) => new(false, false, error);
}

/// <summary>
/// Exclusão definitiva de produto (RN-20, CA-08): o registro e os cinco arquivos de imagem.
///
/// Existe como classe, e não dentro do componente, por uma razão concreta: **a ordem das
/// operações é a decisão da tarefa**, e decisão que vive num manipulador de evento de Razor
/// não tem como ser testada nesta suíte — inverter as duas chamadas passaria despercebido.
/// É a mesma razão que levou o envio de foto a virar <see cref="ProductPhotoUpload"/>.
///
/// **O registro sai primeiro, os arquivos depois.** Se a remoção dos arquivos falhar, sobram
/// objetos órfãos: desperdício, registrado em log com o nome de cada um. Na ordem inversa, uma
/// falha de banco deixaria um produto no acervo **sem imagem alguma**, e a vitrine passaria a
/// exibir item quebrado — entre desperdiçar espaço e exibir item quebrado, o primeiro é menos
/// pior.
/// </summary>
public sealed class ProductRemoval(
    ProductMaintenance maintenance,
    ProductPhotoService photos,
    ILogger<ProductRemoval> logger)
{
    public const string UnavailableMessage =
        "Não foi possível excluir o produto agora. Tente novamente.";

    public async Task<ProductRemovalOutcome> RemoveAsync(
        int productId,
        CancellationToken cancellationToken = default)
    {
        ProductPhoto? photo;

        try
        {
            photo = await maintenance.DeleteAsync(productId, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Nada foi excluído, então a mensagem pode prometer que o produto continua lá.
            logger.LogError(exception, "Falha ao excluir o produto {Id}.", productId);

            return ProductRemovalOutcome.Failed(UnavailableMessage);
        }

        if (photo is null)
        {
            // Sem foto, ou produto que já não existia — nos dois casos não há arquivo a
            // remover, e o estado pedido é o estado atual.
            return new ProductRemovalOutcome(Removed: true, HadPhoto: false);
        }

        // A limpeza não pode reverter a exclusão: o produto já saiu do acervo, que é o que foi
        // pedido. Falha aqui é desperdício registrado, não erro para o dono.
        await photos.DeleteAsync(photo, cancellationToken);

        return new ProductRemovalOutcome(Removed: true, HadPhoto: true);
    }
}
