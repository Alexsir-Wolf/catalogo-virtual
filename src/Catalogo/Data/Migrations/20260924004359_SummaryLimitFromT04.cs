using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalogo.Data.Migrations
{
    /// <inheritdoc />
    public partial class SummaryLimitFromT04 : Migration
    {
        /// <inheritdoc />
        /// <summary>
        /// Estreita o resumo de 160 para 120 caracteres (RN-03, fixado em T-04). O EF sinaliza
        /// risco de perda de dados, e com razão: em banco com produtos cadastrados isto trunca.
        /// Roda aqui porque a tabela ainda está vazia — depois de T-29 a mesma mudança exigiria
        /// decidir o que fazer com o texto excedente de cada produto.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Summary",
                table: "Products",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Summary",
                table: "Products",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldNullable: true);
        }
    }
}
