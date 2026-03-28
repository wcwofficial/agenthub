using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApiKey",
                table: "Agents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Agents_ApiKey",
                table: "Agents",
                column: "ApiKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Agents_ApiKey",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "ApiKey",
                table: "Agents");
        }
    }
}
