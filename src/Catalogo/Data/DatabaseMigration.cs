using Microsoft.EntityFrameworkCore;

namespace Catalogo.Data;

public static class DatabaseMigration
{
    /// <summary>
    /// Aplica as migrations pendentes na subida da aplicação. É a única forma de evoluir
    /// o esquema com um artefato só e um deploy só (ADR-001) — não há passo de release
    /// separado onde rodar o script à mão.
    ///
    /// A operação é idempotente: em banco já atualizado não há pendência e nada é escrito.
    /// </summary>
    public static async Task ApplyPendingMigrationsAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var pending = await context.Database.GetPendingMigrationsAsync();
        if (!pending.Any())
        {
            return;
        }

        app.Logger.LogInformation("Aplicando {Count} migration(s) pendente(s).", pending.Count());
        await context.Database.MigrateAsync();
    }
}
