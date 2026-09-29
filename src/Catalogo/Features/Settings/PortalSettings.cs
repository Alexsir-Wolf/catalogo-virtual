namespace Catalogo.Features.Settings;

/// <summary>
/// Registro **único** de configuração do portal (RN-61). Não há mais de um catálogo, e a
/// ausência de identificador de dono é deliberada (ADR-009) — a linha existe uma vez e é
/// atualizada, nunca inserida de novo.
///
/// Os dados de contato alimentam dois destinos a partir daqui: a vitrine (RN-54) e o
/// rodapé das páginas de conteúdo do PDF (RN-67). Um lugar só, para não divergirem.
/// </summary>
public class PortalSettings
{
    public const int ContactMaxLength = 120;
    public const int CoverFileNameMaxLength = 120;

    /// <summary>
    /// Chave fixa. O registro é único, e uma chave constante é o que torna a leitura um
    /// `Find` e a escrita um `Update` — sem precisar de "pegue o primeiro" em lugar nenhum.
    /// </summary>
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>Número do WhatsApp, apenas dígitos com código do país — é o que a URL de
    /// conversa exige (RN-55).</summary>
    public string? WhatsApp { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    /// <summary>
    /// Nome do objeto da capa no armazenamento. Nulo significa **sem capa**, e sem capa
    /// não há geração de PDF (RN-65) — é a única pista que o dono tem disso, e a UI-10
    /// existe em parte para dá-la.
    /// </summary>
    public string? CoverFileName { get; set; }

    public bool HasCover => CoverFileName is not null;

    /// <summary>
    /// Verdadeiro quando há pelo menos um canal de contato. A vitrine não inventa canal
    /// que o dono não configurou, e prefere não mostrar bloco nenhum a mostrar um vazio.
    /// </summary>
    public bool HasContact =>
        !string.IsNullOrWhiteSpace(WhatsApp)
        || !string.IsNullOrWhiteSpace(Phone)
        || !string.IsNullOrWhiteSpace(Email);
}
