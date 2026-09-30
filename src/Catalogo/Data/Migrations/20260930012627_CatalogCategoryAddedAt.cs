using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalogo.Data.Migrations
{
    /// <inheritdoc />
    public partial class CatalogCategoryAddedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // O padrão para as linhas que já existem é o mínimo da escala, e isso é deliberado:
            // é a única data que **nunca** é posterior a uma geração, então nenhum critério
            // anterior a esta migração passa a destacar produtos retroativamente. Usar `now()`
            // marcaria todo catálogo já gerado como recém-alterado no primeiro acesso.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AddedAt",
                table: "CatalogCategory",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddedAt",
                table: "CatalogCategory");
        }
    }
}
