using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PostmanClone.ExecutionService.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoryReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EnvironmentId",
                table: "history_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SavedRequestId",
                table: "history_entries",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnvironmentId",
                table: "history_entries");

            migrationBuilder.DropColumn(
                name: "SavedRequestId",
                table: "history_entries");
        }
    }
}
