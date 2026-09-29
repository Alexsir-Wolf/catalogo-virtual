using Catalogo.Features.Account;
using Catalogo.Features.Categories;
using Catalogo.Features.Products;
using Catalogo.Features.Settings;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Catalogo.Data;

/// <summary>
/// Acesso a dados da aplicação. Consumido diretamente, sem repositório genérico nem
/// camada de aplicação por cima (ADR-003).
///
/// Nenhuma entidade carrega identificador de tenant: o catálogo é explicitamente de um
/// dono só, e a ausência é deliberada (ADR-009).
/// </summary>
public class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : IdentityDbContext<OwnerAccount>(options)
{
    private const string CaseInsensitiveCollation = "nome_sem_caixa";

    /// <summary>
    /// Envelope imutável de <c>unaccent</c>, criado pela migration `UnaccentSearchIndex`.
    /// A função nativa é <c>STABLE</c> porque depende do dicionário instalado, e índice de
    /// expressão exige <c>IMMUTABLE</c> — daí o envelope, que fixa o dicionário.
    ///
    /// Usar a mesma função nos dois lados da comparação é o que mantém a busca e o índice
    /// falando da mesma coisa: normalizar o termo em C# daria um resultado parecido e
    /// divergente nos casos difíceis (ADR-004, RN-49).
    /// </summary>
    [DbFunction("catalogo_unaccent")]
    public static string Unaccent(string value) =>
        throw new NotSupportedException("Só existe traduzida para SQL.");

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Product> Products => Set<Product>();

    /// <summary>Registro único de configuração do portal (RN-61).</summary>
    public DbSet<PortalSettings> PortalSettings => Set<PortalSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("unaccent");
        modelBuilder.HasPostgresExtension("pg_trgm");

        // Collation não determinística: comparação ignora maiúsculas e minúsculas. É o
        // que faz a unicidade da RN-23 valer de verdade — sem isso o banco aceita
        // "Tintas" e "tintas" como categorias distintas, e o PDF imprime duas seções.
        modelBuilder.HasCollation(
            CaseInsensitiveCollation,
            locale: "und-u-ks-level2",
            provider: "icu",
            deterministic: false);

        modelBuilder.Entity<Category>(category =>
        {
            category.Property(entity => entity.Name)
                .HasMaxLength(Category.NameMaxLength)
                .UseCollation(CaseInsensitiveCollation)
                .IsRequired();

            category.HasIndex(entity => entity.Name).IsUnique();

            category.HasIndex(entity => entity.Position);
        });

        modelBuilder.Entity<PortalSettings>(settings =>
        {
            settings.Property(entity => entity.WhatsApp)
                .HasMaxLength(Features.Settings.PortalSettings.ContactMaxLength);

            settings.Property(entity => entity.Phone)
                .HasMaxLength(Features.Settings.PortalSettings.ContactMaxLength);

            settings.Property(entity => entity.Email)
                .HasMaxLength(Features.Settings.PortalSettings.ContactMaxLength);

            settings.Property(entity => entity.CoverFileName)
                .HasMaxLength(Features.Settings.PortalSettings.CoverFileNameMaxLength);

            // Nada aqui é calculado a partir de outra coluna, e o registro é único — a
            // chave fixa é a garantia de unicidade, e o banco não precisa de mais nada.
            settings.Ignore(entity => entity.HasCover);
            settings.Ignore(entity => entity.HasContact);
        });

        modelBuilder.Entity<Product>(product =>
        {
            product.Property(entity => entity.Name)
                .HasMaxLength(Product.NameMaxLength)
                .IsRequired();

            product.Property(entity => entity.Summary)
                .HasMaxLength(Product.SummaryMaxLength);

            product.Property(entity => entity.Description);

            product.Property(entity => entity.Price)
                .HasPrecision(10, 2);

            product.Property(entity => entity.PriceLabel)
                .HasConversion<string>()
                .HasMaxLength(20);

            product.Property(entity => entity.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            product.HasOne(entity => entity.Category)
                .WithMany()
                .HasForeignKey(entity => entity.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // Um produto pertence a exatamente uma categoria (RN-08), e a ordem é
            // posicional dentro dela (RN-21) — é assim que a vitrine e o PDF leem.
            product.HasIndex(entity => new { entity.CategoryId, entity.Position });

            // O índice que serve à busca da vitrine é sobre a **expressão**
            // `catalogo_unaccent("Name")`, e não sobre a coluna crua: a consulta da
            // RN-49 normaliza o acento nos dois lados, e um índice sobre a coluna não é
            // utilizável por ela (ADR-004; R-01 de REVIEW-T-06-2026-09-23).
            //
            // Índice de expressão não é expressável no modelo, então ele vive no SQL da
            // migration `UnaccentSearchIndex` junto da função imutável que a expressão
            // exige — `unaccent` nativa é STABLE, e índice pede IMMUTABLE.

            product.OwnsOne(entity => entity.Photo, photo =>
            {
                photo.Property(derivative => derivative.OriginalFileName)
                    .HasMaxLength(ProductPhoto.FileNameMaxLength);
                photo.Property(derivative => derivative.ThumbnailFileName)
                    .HasMaxLength(ProductPhoto.FileNameMaxLength);
                photo.Property(derivative => derivative.CardFileName)
                    .HasMaxLength(ProductPhoto.FileNameMaxLength);
                photo.Property(derivative => derivative.LargeFileName)
                    .HasMaxLength(ProductPhoto.FileNameMaxLength);
                photo.Property(derivative => derivative.PrintFileName)
                    .HasMaxLength(ProductPhoto.FileNameMaxLength);
            });
        });
    }
}
