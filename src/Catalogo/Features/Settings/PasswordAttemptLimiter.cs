namespace Catalogo.Features.Settings;

/// <summary>
/// Limita as tentativas de confirmação da senha atual na troca de senha (RN-60).
///
/// Por que não usa o bloqueio do Identity: o contador dele é **o mesmo** do login, e chamar
/// `AccessFailedAsync` aqui deixava o dono fora da tela de acesso por cinco erros de
/// digitação — e permitia que quem tivesse uma sessão aberta o mantivesse **permanentemente**
/// fora do login, disparando falhas de tempo em tempo. Trocar tentativa ilimitada por negação
/// de serviço contra o dono não é correção (R-12 de `REVIEW-T-31-2026-09-29`, segunda rodada).
///
/// O estado vive em memória, e isso é suficiente para o que ele protege: o ataque que importa
/// é o de quem já está autenticado tentando adivinhar a senha atual em sequência, e um teto
/// por janela o interrompe. Reiniciar a aplicação zera a contagem — o dono não fica preso a
/// um bloqueio que ele não tem como levantar, e o atacante ganha, no máximo, mais um punhado
/// de tentativas a cada reinício.
/// </summary>
public sealed class PasswordAttemptLimiter(TimeProvider? time = null)
{
    public const int MaxAttempts = 5;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private readonly TimeProvider time = time ?? TimeProvider.System;
    private readonly Lock gate = new();

    private int failures;
    private DateTimeOffset windowStart;

    public bool IsBlocked
    {
        get
        {
            lock (gate)
            {
                return CurrentFailures() >= MaxAttempts;
            }
        }
    }

    /// <summary>Uma tentativa malsucedida. A janela recomeça quando expira.</summary>
    public void RegisterFailure()
    {
        lock (gate)
        {
            failures = CurrentFailures() + 1;

            if (failures == 1)
            {
                windowStart = time.GetUtcNow();
            }
        }
    }

    /// <summary>
    /// Acerto zera a contagem: erros espalhados ao longo do tempo não podem somar até
    /// bloquear o dono legítimo.
    /// </summary>
    public void Reset()
    {
        lock (gate)
        {
            failures = 0;
        }
    }

    private int CurrentFailures() =>
        failures > 0 && time.GetUtcNow() - windowStart >= Window ? 0 : failures;
}
