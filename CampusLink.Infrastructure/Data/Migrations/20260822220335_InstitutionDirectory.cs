using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusLink.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InstitutionDirectory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Country",
                table: "Institutions",
                newName: "CountryCode");

            migrationBuilder.RenameIndex(
                name: "IX_Institutions_Country_Name",
                table: "Institutions",
                newName: "IX_Institutions_CountryCode_Name");

            migrationBuilder.RenameColumn(
                name: "Country",
                table: "AspNetUsers",
                newName: "CountryCode");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.AddColumn<bool>(
                name: "IsUserSubmitted",
                table: "Institutions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Institutions_IsVerified_IsUserSubmitted",
                table: "Institutions",
                columns: new[] { "IsVerified", "IsUserSubmitted" });

            migrationBuilder.CreateIndex(
                name: "IX_Institutions_Name_Trgm",
                table: "Institutions",
                column: "Name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Institutions_ShortName_Trgm",
                table: "Institutions",
                column: "ShortName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Institutions_IsVerified_IsUserSubmitted",
                table: "Institutions");

            migrationBuilder.DropIndex(
                name: "IX_Institutions_Name_Trgm",
                table: "Institutions");

            migrationBuilder.DropIndex(
                name: "IX_Institutions_ShortName_Trgm",
                table: "Institutions");

            migrationBuilder.DropColumn(
                name: "IsUserSubmitted",
                table: "Institutions");

            migrationBuilder.RenameColumn(
                name: "CountryCode",
                table: "Institutions",
                newName: "Country");

            migrationBuilder.RenameIndex(
                name: "IX_Institutions_CountryCode_Name",
                table: "Institutions",
                newName: "IX_Institutions_Country_Name");

            migrationBuilder.RenameColumn(
                name: "CountryCode",
                table: "AspNetUsers",
                newName: "Country");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
