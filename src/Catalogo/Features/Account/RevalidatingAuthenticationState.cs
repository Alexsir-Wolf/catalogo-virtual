using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;

namespace Catalogo.Features.Account;

/// <summary>
/// Revalida a identidade do circuito interativo contra o banco, em intervalo curto.
///
/// O gate do painel é middleware por caminho: ele roda na requisição HTTP inicial e não vê
/// as interações, que trafegam por `/_blazor`. Sem revalidação, uma aba aberta continuava
/// operando depois de a senha mudar, de o cookie expirar ou de a sessão ser encerrada — o
/// que esvaziava, dentro daquela aba, tanto a validação de selo quanto a saída do painel
/// (R-09 de `REVIEW-T-31-2026-09-29`).
///
/// A revalidação é pelo **selo de segurança**, o mesmo critério do cookie: um só lugar
/// decide se a identidade ainda vale.
/// </summary>
public sealed class RevalidatingAuthenticationState(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    /// <summary>
    /// Curto porque o circuito é de uma pessoa só e a consulta é por chave primária. É o
    /// atraso máximo entre encerrar a sessão e a aba aberta perceber.
    /// </summary>
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OwnerAccount>>();

        var owner = await users.GetUserAsync(authenticationState.User);
        if (owner is null)
        {
            return false;
        }

        if (!users.SupportsUserSecurityStamp)
        {
            return true;
        }

        var stamp = authenticationState.User.FindFirstValue(
            users.Options.ClaimsIdentity.SecurityStampClaimType);

        return stamp == await users.GetSecurityStampAsync(owner);
    }
}
