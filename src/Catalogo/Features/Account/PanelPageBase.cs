using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace Catalogo.Features.Account;

/// <summary>
/// Base das páginas interativas do painel. Dá a elas um único jeito de recusar escrita quando a
/// sessão morreu debaixo do circuito.
///
/// Por que uma base e não sete cópias do mesmo `if`: são dezesseis pontos de escrita em seis telas,
/// e a decisão — o que conta como sessão viva, para onde mandar quem perdeu a dela — precisa ser
/// uma. Sete cópias divergem na primeira vez que alguém corrigir só a que estava olhando.
///
/// O que ela **não** é: substituta do gate por caminho nem do `[Authorize]` das páginas. Esses
/// dois valem na requisição HTTP e continuam sendo a primeira barreira, para que tela nova não
/// nasça aberta. Esta é a terceira camada, e a única que o circuito interativo alcança
/// (R-01 de `REVIEW-T-31-2026-09-30-round3`).
/// </summary>
public abstract class PanelPageBase : ComponentBase
{
    [CascadingParameter]
    protected Task<AuthenticationState>? Authentication { get; set; }

    [Inject]
    protected NavigationManager Navigation { get; set; } = default!;

    /// <summary>
    /// Chamada no início de **toda** escrita. Devolve falso quando a sessão não vale mais, e nesse
    /// caso já levou o visitante para a tela de acesso — quem chamou só precisa sair do método.
    ///
    /// `forceLoad` porque o estado morto vive **dentro** do circuito atual: navegar por dentro dele
    /// preservaria o mesmo estado, e a próxima interação tentaria gravar de novo. Recarregar derruba
    /// o circuito e refaz a requisição pelo caminho HTTP, onde o cookie já não vale e o gate por
    /// caminho responde.
    ///
    /// A ordem importa: a recusa acontece **antes** de qualquer efeito, nunca depois de gravar e
    /// antes de confirmar na tela. É a diferença entre recusar e desfazer.
    /// </summary>
    protected async Task<bool> SessionIsAliveAsync()
    {
        var state = Authentication is null ? null : await Authentication;

        if (PanelSession.IsAlive(state))
        {
            return true;
        }

        Navigation.NavigateTo(LoginPathWithReturn(), forceLoad: true);

        return false;
    }

    /// <summary>
    /// A tela de acesso com o `retorno` para onde o dono estava, montado como o gate por caminho
    /// monta (`UsePanelAuthorization`) — mesmo parâmetro, mesmo escape.
    ///
    /// Sem isso, quem perdia a sessão editando `/painel/produtos/42` reautenticava e caía na raiz do
    /// painel, perdendo o contexto. Enquanto esta guarda não existia o desvio era inócuo, porque o
    /// único caminho que redirecionava era inalcançável; agora é o caminho normal
    /// (A-05 de `REVIEW-T-31-2026-09-30-round4`).
    ///
    /// O destino é **relativo e derivado do próprio endereço atual**, nunca de entrada do visitante:
    /// é o que impede esta construção de virar o mesmo redirecionamento aberto que o `retorno` da
    /// tela de acesso tem (R-05 de `REVIEW-T-31-2026-09-30-round2`).
    /// </summary>
    private string LoginPathWithReturn()
    {
        var current = Navigation.ToBaseRelativePath(Navigation.Uri);
        var returnUrl = Uri.EscapeDataString($"/{current}");

        return $"{PanelAuthentication.LoginPath}?{PanelAuthentication.ReturnUrlParameter}={returnUrl}";
    }
}
