using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskPollIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TargetAgentId_Status_CreatedAtUtc",
                table: "Tasks",
                columns: new[] { "TargetAgentId", "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_TargetAgentId_Status_CreatedAtUtc",
                table: "Tasks");
        }
    }
}
