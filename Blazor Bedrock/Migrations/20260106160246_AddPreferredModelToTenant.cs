using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blazor_Bedrock.Migrations
{
    /// <inheritdoc />
    public partial class AddPreferredModelToTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreferredModel",
                table: "Tenants",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreferredModel",
                table: "Tenants");
        }
    }
}
