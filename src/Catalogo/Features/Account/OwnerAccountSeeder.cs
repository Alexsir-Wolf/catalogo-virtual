using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Catalogo.Features.Account;

/// <summary>
/// Cria a conta do dono na primeira subida. Não há registro público nem convite: esta é
/// a única porta de entrada de uma credencial no sistema (RN-57, ADR-006).
/// </summary>
public static class OwnerAccountSeeder
{
    public static async Task SeedOwnerAccountAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();

        var options = scope.ServiceProvider.GetRequiredService<IOptions<OwnerAccountOptions>>().Value;
        if (!options.IsConfigured)
        {
            app.Logger.LogWarning(
                "Conta do dono não semeada: a seção '{Section}' não traz usuário e senha. " +
                "O painel fica inacessível até a credencial ser configurada.",
                OwnerAccountOptions.SectionName);
            return;
        }

        var users = scope.ServiceProvider.GetRequiredService<UserManager<OwnerAccount>>();
        if (await users.FindByNameAsync(options.UserName) is not null)
        {
            return;
        }

        var owner = new OwnerAccount
        {
            UserName = options.UserName,
            MustChangePassword = true
        };

        var result = await users.CreateAsync(owner, options.Password);
        if (!result.Succeeded)
        {
            // Os códigos descrevem qual política a senha violou; nenhum deles traz o valor.
            throw new InvalidOperationException(
                "Falha ao semear a conta do dono: " +
                string.Join(", ", result.Errors.Select(error => error.Code)));
        }

        app.Logger.LogInformation("Conta do dono semeada com troca de senha pendente.");
    }
}
