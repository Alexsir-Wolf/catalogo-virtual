using Catalogo.Features.Account;
using Catalogo.Features.Categories;
using Catalogo.Features.Products;
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
    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("unaccent");
        modelBuilder.HasPostgresExtension("pg_trgm");

        modelBuilder.Entity<Category>(category =>
        {
            category.Property(entity => entity.Name)
                .HasMaxLength(Category.NameMaxLength)
                .IsRequired();

            category.HasIndex(entity => entity.Name).IsUnique();

            category.HasIndex(entity => entity.Position);
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

            // Busca textual tolerante a erro de digitação sem motor de busca dedicado
            // (ADR-004). O índice trigrama é o que torna o `like` da vitrine viável.
            product.HasIndex(entity => entity.Name)
                .HasMethod("gin")
                .HasOperators("gin_trgm_ops");

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
