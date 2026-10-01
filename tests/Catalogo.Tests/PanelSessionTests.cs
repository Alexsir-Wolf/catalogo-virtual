using System.Security.Claims;
using Catalogo.Features.Account;
using Microsoft.AspNetCore.Components.Authorization;

namespace Catalogo.Tests;

/// <summary>
/// A regra que decide se a sessão do painel ainda vale dentro do circuito interativo.
///
/// Por que estes casos existem: três rounds de review de T-31 condenaram a mesma coisa — o efeito
/// prometido não tinha teste que o observasse, então nada contradizia a declaração de que estava
/// pronto. Duas defesas foram construídas e ligadas no lugar errado antes de alguém perceber: o
/// `SecurityStampValidator`, que vale só na borda HTTP, e o `AuthorizeRouteView`, que ficou no
/// renderizador de SSR e nunca entrou no circuito
/// (R-01 de `REVIEW-T-31-2026-09-30-round3`).
///
/// `PanelSession` é pura justamente para que a regra possa ser verificada sem banco, sem navegador
/// e sem circuito — e por isso estes casos rodam em milissegundos e não dependem de Docker, que é
/// o que faltava para o elo ser cobrado.
/// </summary>
public sealed class PanelSessionTests
{
    private const string AuthenticationType = "Cookies";

    [Fact]
    public void Dono_autenticado_tem_sessao_viva()
    {
        var state = new AuthenticationState(Authenticated("dono"));

        Assert.True(PanelSession.IsAlive(state));
    }

    /// <summary>
    /// O estado que `ForceSignOut` instala quando o selo de segurança deixa de valer: principal
    /// com identidade **não** autenticada. É o cenário do logout em outro dispositivo, e é o que
    /// precisa recusar a escrita.
    /// </summary>
    [Fact]
    public void Estado_anonimo_tem_sessao_morta()
    {
        var state = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.False(PanelSession.IsAlive(state));
    }

    /// <summary>
    /// Ausência conta como morta, e não como "ainda não carregou". Assumir válido por falta de
    /// informação é exatamente como o defeito dos rounds anteriores se comportava.
    /// </summary>
    [Fact]
    public void Estado_ausente_tem_sessao_morta()
    {
        Assert.False(PanelSession.IsAlive((AuthenticationState?)null));
    }

    [Fact]
    public void Principal_ausente_tem_sessao_morta()
    {
        Assert.False(PanelSession.IsAlive((ClaimsPrincipal?)null));
    }

    /// <summary>
    /// Principal sem identidade nenhuma — não é o mesmo objeto que o anônimo do framework, e
    /// `Identity` devolve nulo em vez de uma identidade não autenticada.
    /// </summary>
    [Fact]
    public void Principal_sem_identidade_tem_sessao_morta()
    {
        Assert.False(PanelSession.IsAlive(new ClaimsPrincipal()));
    }

    /// <summary>
    /// Um nome no claim não basta: o que decide é `IsAuthenticated`, que depende do tipo de
    /// autenticação ter sido informado. Sem ele, o Identity considera a identidade não
    /// autenticada — e é assim que um principal montado à mão se parece.
    /// </summary>
    [Fact]
    public void Identidade_com_nome_mas_sem_autenticacao_tem_sessao_morta()
    {
        var unauthenticated = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Name, "dono")]));

        Assert.False(PanelSession.IsAlive(unauthenticated));
    }

    private static ClaimsPrincipal Authenticated(string userName) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, userName)], AuthenticationType));
}
