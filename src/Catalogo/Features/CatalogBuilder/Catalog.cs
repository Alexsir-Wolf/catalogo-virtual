using Catalogo.Features.Categories;

namespace Catalogo.Features.CatalogBuilder;

/// <summary>
/// Um catálogo é **nome e categorias**, e nada mais (RN-26, ADR-014).
///
/// A ausência mais importante deste tipo é a que não se vê: **não existe lista de produtos**
/// (RN-29). Essa ausência é a decisão, não um detalhe de modelagem — é o que faz um catálogo
/// salvo em março, gerado em outubro, sair com o acervo de outubro. Persistir a lista
/// "para performance" reverteria a ADR-014 e traria de volta exatamente a dor que o projeto
/// veio resolver: o catálogo que envelhece sozinho.
///
/// A contagem de produtos também não vive aqui. Ela é resolvida na hora de exibir (RN-30) e
/// muda entre duas visitas sem ninguém ter mexido no catálogo.
/// </summary>
public class Catalog
{
    public const int NameMaxLength = 120;

    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Quando este catálogo foi gerado pela última vez, ou nulo se nunca foi (RN-33). Nulo é
    /// o estado `UI-07.nuncaGerado`, e a tela marca a ausência em vez de inventar data.
    /// </summary>
    public DateTimeOffset? LastGeneratedAt { get; set; }

    public bool WasGenerated => LastGeneratedAt is not null;

    public List<CatalogCategory> Categories { get; set; } = [];
}

/// <summary>
/// Uma categoria escolhida por um catálogo. É a única coisa que o catálogo persiste além do
/// nome, e é o critério — não o resultado.
/// </summary>
public class CatalogCategory
{
    public int CatalogId { get; set; }

    public Catalog Catalog { get; set; } = null!;

    public int CategoryId { get; set; }

    public Category Category { get; set; } = null!;

    /// <summary>
    /// Quando esta categoria entrou no critério.
    ///
    /// Existe por causa da RN-32, que fala de produtos que passaram a **integrar o catálogo**
    /// desde a última geração — e isso não é a mesma coisa que produtos publicados desde então.
    /// Acrescentar uma categoria a um critério traz para dentro do catálogo todos os produtos
    /// dela, publicados meses antes: sem esta data, nenhum seria destacado, e o dono descobriria
    /// no cliente que o catálogo cresceu. É exatamente o risco que a RN-32 existe para mitigar.
    ///
    /// **Continua sendo critério, não lista de produtos** — a ADR-014 fica intacta: o que se
    /// data é a escolha do dono, não o resultado dela.
    /// </summary>
    public DateTimeOffset AddedAt { get; set; }
}
