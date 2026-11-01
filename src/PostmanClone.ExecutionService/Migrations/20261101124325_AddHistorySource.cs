using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PostmanClone.ExecutionService.Migrations
{
    /// <inheritdoc />
    public partial class AddHistorySource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "history_entries",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "server");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                table: "history_entries");
        }
    }
}
