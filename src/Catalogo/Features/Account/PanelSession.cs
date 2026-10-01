using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Catalogo.Features.Account;

/// <summary>
/// Diz se a sessão do painel ainda vale, do ponto de vista do **circuito interativo**.
///
/// Por que isto existe como peça própria, e não como um `if` dentro de cada tela: o gate por
/// caminho (`UsePanelAuthorization`) decide na requisição HTTP, e o `[Authorize]` das páginas é
/// enforçado pelo mesmo caminho. Nenhum dos dois vê as interações, que trafegam por `/_blazor` —
/// e o `AuthorizeRouteView` do roteador também não, porque neste projeto a interatividade é **por
/// página**: `App.razor` renderiza `Routes` sem render mode, então o roteador vive no renderizador
/// de SSR da requisição e o componente raiz do circuito é a própria página
/// (R-01 de `REVIEW-T-31-2026-09-30-round3`).
///
/// Sobrou um único ponto que o circuito realmente alcança: o estado de autenticação cascateado,
/// que `RevalidatingAuthenticationState` troca por anônimo quando o selo de segurança deixa de
/// valer. Quem precisa olhar para ele é cada escrita, e a regra de olhar é esta classe — uma só,
/// para que as dezesseis chamadas não sejam dezesseis interpretações diferentes de "ainda vale".
///
/// A regra é pura de propósito: sem `NavigationManager`, sem componente, sem circuito. É o que a
/// torna verificável sem banco e sem navegador, que era a lacuna que atravessou três rounds de
/// review — o efeito prometido não tinha teste que o observasse.
/// </summary>
public static class PanelSession
{
    /// <summary>
    /// Verdadeiro quando o estado cascateado ainda traz uma identidade autenticada.
    ///
    /// Estado ausente conta como morto, e não como "ainda não carregou": a ausência só acontece
    /// se alguém esquecer o `CascadingAuthenticationState`, e nesse caso recusar a escrita é a
    /// falha segura. O contrário — assumir válido por falta de informação — é exatamente como o
    /// defeito dos rounds anteriores se comportava.
    /// </summary>
    public static bool IsAlive(AuthenticationState? state) => IsAlive(state?.User);

    /// <summary>
    /// A mesma regra sobre o principal, para quem já o tem em mão.
    /// </summary>
    public static bool IsAlive(ClaimsPrincipal? user) =>
        user?.Identity is { IsAuthenticated: true };
}
