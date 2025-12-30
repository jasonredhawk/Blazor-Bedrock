using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blazor_Bedrock.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLastSelectedTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastSelectedTenantId",
                table: "Users",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSelectedTenantId",
                table: "Users");
        }
    }
}
