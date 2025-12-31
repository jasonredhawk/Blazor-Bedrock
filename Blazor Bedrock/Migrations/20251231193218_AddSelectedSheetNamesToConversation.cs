using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blazor_Bedrock.Migrations
{
    /// <inheritdoc />
    public partial class AddSelectedSheetNamesToConversation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SelectedSheetNames",
                table: "ChatGptConversations",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SelectedSheetNames",
                table: "ChatGptConversations");
        }
    }
}
