using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalogo.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProductCategorizedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // O padrão para as linhas existentes é o mínimo da escala, e isso é deliberado: é a
            // única data que nunca é posterior a uma geração, então nenhum produto já cadastrado
            // passa a ser destacado retroativamente. Usar `now()` marcaria o acervo inteiro como
            // recém-movido no primeiro acesso, que é o oposto do que a RN-32 quer comunicar.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CategorizedAt",
                table: "Products",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CategorizedAt",
                table: "Products");
        }
    }
}
