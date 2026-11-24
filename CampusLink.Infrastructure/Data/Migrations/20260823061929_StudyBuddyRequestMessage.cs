using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusLink.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class StudyBuddyRequestMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Message",
                table: "StudyBuddyMatches",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Message",
                table: "StudyBuddyMatches");
        }
    }
}
