using Microsoft.AspNetCore.Identity;

namespace Catalogo.Features.Account;

/// <summary>
/// O único usuário do sistema (RN-57). Não há registro, convite, papéis nem perfis —
/// a autorização do painel é binária: estar autenticado (ADR-006).
/// </summary>
public class OwnerAccount : IdentityUser
{
    /// <summary>
    /// A conta nasce semeada, com senha vinda de configuração. Enquanto não for trocada,
    /// o painel cobra a troca no primeiro acesso (ADR-006).
    /// </summary>
    public bool MustChangePassword { get; set; }
}
