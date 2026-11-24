using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusLink.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptionalStudentNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_InstitutionId_StudentNumber",
                table: "AspNetUsers");

            // Accounts registered without a student number were stored as "",
            // which the filtered index would still treat as a real value.
            migrationBuilder.Sql(
                """
                UPDATE "AspNetUsers"
                SET "StudentNumber" = NULL
                WHERE "StudentNumber" IS NOT NULL AND btrim("StudentNumber") = '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_InstitutionId_StudentNumber",
                table: "AspNetUsers",
                columns: new[] { "InstitutionId", "StudentNumber" },
                unique: true,
                filter: "\"StudentNumber\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_InstitutionId_StudentNumber",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_InstitutionId_StudentNumber",
                table: "AspNetUsers",
                columns: new[] { "InstitutionId", "StudentNumber" },
                unique: true);
        }
    }
}
