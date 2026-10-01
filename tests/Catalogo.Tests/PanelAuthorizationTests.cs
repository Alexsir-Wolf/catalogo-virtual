using Catalogo.Features.Account;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace Catalogo.Tests;

/// <summary>
/// Autorização das páginas do painel, por estrutura: quem exige `[Authorize]` e quem deriva da base
/// que carrega a guarda do circuito.
///
/// Por que estes casos existem, e por que são estruturais: `RevalidatingAuthenticationState`
/// confere o selo de segurança e troca o estado por anônimo quando ele deixa de valer, mas
/// `ForceSignOut` do framework não encerra o circuito nem navega — só troca o estado cascateado.
/// Enquanto nenhum componente reagia a essa troca, uma aba aberta continuava salvando contato e
/// trocando a capa pública depois de o dono encerrar a sessão em outro dispositivo
/// (R-02 de `REVIEW-T-31-2026-09-30-round2`).
///
/// **O que estes casos cobrem, e o que não cobrem** — dito com precisão porque a primeira versão
/// deste comentário prometia mais do que entrega: eles afirmam que o `[Authorize]` **existe** em
/// cada página sob `/painel`, e falham se alguém o remover. Isso é tudo. Presença de anotação não
/// é prova de bloqueio: o que o atributo faz hoje é virar metadata de endpoint, enforçada por
/// `UseAuthorization` na requisição HTTP.
///
/// Em particular, estes casos **não** cobrem o `AuthorizeRouteView` de `Routes.razor` nem o
/// `@using` que o torna um componente em vez de markup literal — trocá-lo de volta por `RouteView`
/// os deixa todos verdes. Foi assim que o round 3 encontrou uma correção declarada e inerte.
///
/// O bloqueio **dentro do circuito**, que é o defeito que atravessou três rounds, tem caso próprio
/// e de comportamento: `PanelPageGuardTests` e `PanelSessionTests`. O gate por caminho segue
/// coberto por `PanelAccessTests`, pelo lado HTTP. As três camadas, três suítes.
/// </summary>
public sealed class PanelAuthorizationTests
{
    private const string PanelRoutePrefix = "/painel";

    private static readonly Assembly Application = typeof(Program).Assembly;

    /// <summary>
    /// Toda página sob `/painel` exige autorização — exceto as duas que não podem exigir, e a
    /// razão de cada uma está no caso que as nomeia.
    /// </summary>
    [Theory]
    [InlineData("Catalogo.Features.Panel.PanelHome")]
    [InlineData("Catalogo.Features.Products.ProductList")]
    [InlineData("Catalogo.Features.Products.ProductForm")]
    [InlineData("Catalogo.Features.Categories.CategoryList")]
    [InlineData("Catalogo.Features.CatalogBuilder.CatalogList")]
    [InlineData("Catalogo.Features.CatalogBuilder.CatalogPage")]
    [InlineData("Catalogo.Features.Settings.SettingsPage")]
    public void Pagina_do_painel_exige_autorizacao(string typeName)
    {
        var page = Application.GetType(typeName);

        Assert.NotNull(page);
        Assert.NotEmpty(page.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
    }

    /// <summary>
    /// A tela de acesso **não** pode exigir autorização: ela é onde se obtém a identidade, e
    /// exigi-la ali produz o laço de redirecionar para a própria tela.
    /// </summary>
    [Fact]
    public void Tela_de_acesso_nao_exige_autorizacao()
    {
        var login = Application.GetType("Catalogo.Features.Account.Login");

        Assert.NotNull(login);
        Assert.Empty(login.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
    }

    /// <summary>
    /// A saída também não, e a razão registrada antes estava errada: ela dizia que quem chega com
    /// o estado anônimo "precisa alcançar a tela", e `PanelAccessTests` prova o contrário — um
    /// `GET /painel/sair` anônimo responde `Redirect`, pelo gate por prefixo
    /// (R-09 de `REVIEW-T-31-2026-09-30-round3`).
    ///
    /// A isenção fica porque é inofensiva e porque o componente não é a defesa: a saída é SSR
    /// estática, é `POST` com `AntiforgeryToken`, e o gate por prefixo já a fecha. Anotar
    /// `[Authorize]` aqui não acrescentaria barreira, só mais um lugar para desalinhar.
    /// </summary>
    [Fact]
    public void Tela_de_saida_nao_exige_autorizacao_no_componente()
    {
        var logout = Application.GetType("Catalogo.Features.Account.Logout");

        Assert.NotNull(logout);
        Assert.Empty(logout.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
    }

    /// <summary>
    /// Nenhuma página do painel escapa da lista acima. Sem esta varredura, tela nova nasceria
    /// aberta no circuito e os casos nomeados continuariam verdes — é a diferença entre cobrir
    /// o que existe hoje e cobrir a regra.
    /// </summary>
    [Fact]
    public void Nenhuma_pagina_do_painel_fica_sem_autorizacao()
    {
        var exemptions = new[]
        {
            "Catalogo.Features.Account.Login",
            "Catalogo.Features.Account.Logout"
        };

        var unprotected = Application.GetTypes()
            .Where(type => typeof(IComponent).IsAssignableFrom(type))
            .Where(type => type.GetCustomAttributes<RouteAttribute>(inherit: true).Any(IsPanelRoute))
            .Where(type => !exemptions.Contains(type.FullName))
            .Where(type => !type.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any())
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(unprotected);
    }

    /// <summary>
    /// Toda página interativa do painel deriva de <see cref="PanelPageBase"/>, que é de onde vem a
    /// guarda de escrita do circuito.
    ///
    /// Esta é a varredura irmã da de `[Authorize]`, e existe pela mesma razão: cobrir a **regra** e
    /// não as páginas de hoje. Sem ela, tela nova nasceria com escrita e sem guarda, e a suíte
    /// continuaria verde — que é exatamente o padrão que os rounds 1 a 3 de T-31 condenaram.
    ///
    /// `PanelHome` é a exceção e está nomeada: é a única página do painel que não grava nada, então
    /// não tem ponto de escrita para guardar. No dia em que ela ganhar um, este caso cai.
    /// </summary>
    [Fact]
    public void Pagina_interativa_do_painel_deriva_da_base_com_guarda()
    {
        var withoutGuard = Application.GetTypes()
            .Where(type => typeof(IComponent).IsAssignableFrom(type))
            .Where(type => type.GetCustomAttributes<RouteAttribute>(inherit: true).Any(IsPanelRoute))
            .Where(type => type.GetCustomAttributes<RenderModeAttribute>(inherit: true).Any())
            .Where(type => type.FullName != "Catalogo.Features.Panel.PanelHome")
            .Where(type => !typeof(PanelPageBase).IsAssignableFrom(type))
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(withoutGuard);
    }

    /// <summary>
    /// Rota do painel é o prefixo **por segmento** e sem distinguir caixa, como o gate de
    /// `PanelAuthentication` decide — comparar texto puro deixava `/Painel/nova` escapar e cobraria
    /// de uma rota pública chamada `/painelpublico`
    /// (R-08 de `REVIEW-T-31-2026-09-30-round3`).
    /// </summary>
    private static bool IsPanelRoute(RouteAttribute route) =>
        route.Template.Equals(PanelRoutePrefix, StringComparison.OrdinalIgnoreCase)
        || route.Template.StartsWith($"{PanelRoutePrefix}/", StringComparison.OrdinalIgnoreCase);
}
