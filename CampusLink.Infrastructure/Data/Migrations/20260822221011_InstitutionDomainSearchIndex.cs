using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusLink.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InstitutionDomainSearchIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_InstitutionEmailDomains_Domain_Trgm",
                table: "InstitutionEmailDomains",
                column: "Domain")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstitutionEmailDomains_Domain_Trgm",
                table: "InstitutionEmailDomains");
        }
    }
}
