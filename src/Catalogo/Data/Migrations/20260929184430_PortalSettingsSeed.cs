using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalogo.Data.Migrations
{
    /// <summary>
    /// Semeia o registro único de configuração (RN-61).
    ///
    /// A linha existia porque a primeira leitura a inseria — e a leitura acontece no layout
    /// da vitrine, que é público e anônimo: dois visitantes simultâneos num banco recém
    /// implantado disputavam a chave fixa e um recebia violação de unicidade, virando 500 na
    /// página pública (R-04 de `REVIEW-T-31-2026-09-29`). Com a linha semeada aqui, a
    /// leitura deixa de precisar escrever.
    /// </summary>
    public partial class PortalSettingsSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // `ON CONFLICT DO NOTHING` porque bancos que já rodaram a versão anterior têm a
            // linha criada em tempo de execução: a migration precisa ser idempotente sobre
            // eles, não só sobre banco novo.
            migrationBuilder.Sql(
                """
                INSERT INTO "PortalSettings" ("Id", "WhatsApp", "Phone", "Email", "CoverFileName")
                VALUES (1, NULL, NULL, NULL, NULL)
                ON CONFLICT ("Id") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Apagar a linha desfaria a configuração do dono junto com o seed. O registro é
            // único e reaparece idêntico no `Up`, então não há o que reverter.
        }
    }
}
