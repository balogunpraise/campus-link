using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusLink.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TwoPartyLoanConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BorrowerConfirmedReceiptAt",
                table: "ResourceShareRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BorrowerConfirmedReturnAt",
                table: "ResourceShareRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisputeReason",
                table: "ResourceShareRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DisputedAt",
                table: "ResourceShareRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OwnerConfirmedHandoverAt",
                table: "ResourceShareRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OwnerConfirmedReturnAt",
                table: "ResourceShareRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceShareRequests_Status_DueDate",
                table: "ResourceShareRequests",
                columns: new[] { "Status", "DueDate" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShareRequest_ReturnAfterHandover",
                table: "ResourceShareRequests",
                sql: "(\"BorrowerConfirmedReturnAt\" IS NULL AND \"OwnerConfirmedReturnAt\" IS NULL)\nOR (\"OwnerConfirmedHandoverAt\" IS NOT NULL AND \"BorrowerConfirmedReceiptAt\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShareRequest_ReturnedAtRequiresBothConfirmations",
                table: "ResourceShareRequests",
                sql: "\"ReturnedAt\" IS NULL\nOR (\"BorrowerConfirmedReturnAt\" IS NOT NULL AND \"OwnerConfirmedReturnAt\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ResourceShareRequests_Status_DueDate",
                table: "ResourceShareRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ShareRequest_ReturnAfterHandover",
                table: "ResourceShareRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ShareRequest_ReturnedAtRequiresBothConfirmations",
                table: "ResourceShareRequests");

            migrationBuilder.DropColumn(
                name: "BorrowerConfirmedReceiptAt",
                table: "ResourceShareRequests");

            migrationBuilder.DropColumn(
                name: "BorrowerConfirmedReturnAt",
                table: "ResourceShareRequests");

            migrationBuilder.DropColumn(
                name: "DisputeReason",
                table: "ResourceShareRequests");

            migrationBuilder.DropColumn(
                name: "DisputedAt",
                table: "ResourceShareRequests");

            migrationBuilder.DropColumn(
                name: "OwnerConfirmedHandoverAt",
                table: "ResourceShareRequests");

            migrationBuilder.DropColumn(
                name: "OwnerConfirmedReturnAt",
                table: "ResourceShareRequests");
        }
    }
}
