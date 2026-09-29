using Catalogo.Features.Settings;
using Microsoft.Extensions.Time.Testing;

namespace Catalogo.Tests;

/// <summary>
/// Teto de tentativas contra a senha atual (RN-60), com contador **próprio**. O do Identity é
/// compartilhado com o login: usá-lo trancava o dono fora do acesso por cinco erros de
/// digitação, e permitia a quem tivesse sessão aberta mantê-lo fora indefinidamente
/// (R-12 de `REVIEW-T-31-2026-09-29`, segunda rodada).
/// </summary>
public sealed class PasswordAttemptLimiterTests
{
    [Fact]
    public void Comeca_liberado()
    {
        Assert.False(new PasswordAttemptLimiter().IsBlocked);
    }

    [Fact]
    public void RN_60_bloqueia_ao_alcancar_o_teto_de_tentativas()
    {
        var limiter = new PasswordAttemptLimiter();

        for (var attempt = 0; attempt < PasswordAttemptLimiter.MaxAttempts; attempt++)
        {
            Assert.False(limiter.IsBlocked);
            limiter.RegisterFailure();
        }

        Assert.True(limiter.IsBlocked);
    }

    [Fact]
    public void Acerto_zera_a_contagem()
    {
        var limiter = new PasswordAttemptLimiter();

        for (var attempt = 0; attempt < PasswordAttemptLimiter.MaxAttempts; attempt++)
        {
            limiter.RegisterFailure();
        }

        limiter.Reset();

        Assert.False(limiter.IsBlocked);
    }

    /// <summary>
    /// O bloqueio é temporário: passada a janela, o dono volta a tentar sem depender de
    /// ninguém para levantá-lo.
    /// </summary>
    [Fact]
    public void O_bloqueio_expira_com_a_janela()
    {
        var time = new FakeTimeProvider();
        var limiter = new PasswordAttemptLimiter(time);

        for (var attempt = 0; attempt < PasswordAttemptLimiter.MaxAttempts; attempt++)
        {
            limiter.RegisterFailure();
        }

        Assert.True(limiter.IsBlocked);

        time.Advance(PasswordAttemptLimiter.Window);

        Assert.False(limiter.IsBlocked);
    }

    /// <summary>
    /// Erros espalhados ao longo do tempo **não somam** até bloquear: quatro falhas, a janela
    /// passa, e a contagem recomeça. Sem isso, o dono acumularia bloqueio por uso normal.
    /// </summary>
    [Fact]
    public void Falhas_em_janelas_diferentes_nao_somam()
    {
        var time = new FakeTimeProvider();
        var limiter = new PasswordAttemptLimiter(time);

        for (var attempt = 0; attempt < PasswordAttemptLimiter.MaxAttempts - 1; attempt++)
        {
            limiter.RegisterFailure();
        }

        time.Advance(PasswordAttemptLimiter.Window);

        for (var attempt = 0; attempt < PasswordAttemptLimiter.MaxAttempts - 1; attempt++)
        {
            limiter.RegisterFailure();
        }

        Assert.False(limiter.IsBlocked);
    }
}
