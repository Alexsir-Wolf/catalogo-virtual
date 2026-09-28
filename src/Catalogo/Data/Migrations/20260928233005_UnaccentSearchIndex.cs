using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalogo.Data.Migrations
{
    /// <summary>
    /// Troca o índice trigrama sobre a coluna crua por um índice sobre a expressão que a
    /// busca da vitrine realmente usa (ADR-004, RN-49).
    ///
    /// O índice anterior era inutilizável pela consulta prometida: `unaccent("Name")
    /// ILIKE ...` não alcança um índice construído sobre `"Name"`. Corrige o R-01 de
    /// `REVIEW-T-06-2026-09-23`, que previu o problema aparecendo aqui, em T-18.
    ///
    /// O envelope imutável existe porque `unaccent(text)` é `STABLE` — depende do
    /// dicionário corrente — e índice de expressão exige `IMMUTABLE`. Fixar o dicionário
    /// em `unaccent('unaccent', ...)` remove a dependência e torna a promessa honesta.
    /// </summary>
    public partial class UnaccentSearchIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_Name",
                table: "Products");

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION catalogo_unaccent(texto text)
                RETURNS text
                LANGUAGE sql
                IMMUTABLE
                PARALLEL SAFE
                STRICT
                AS $$ SELECT public.unaccent('public.unaccent'::regdictionary, texto) $$;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX "IX_Products_Name_Unaccent"
                ON "Products"
                USING gin (catalogo_unaccent("Name") gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Products_Name_Unaccent";""");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS catalogo_unaccent(text);");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Name",
                table: "Products",
                column: "Name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }
    }
}
