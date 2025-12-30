using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blazor_Bedrock.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPromptIdToConversation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PromptId",
                table: "ChatGptConversations",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PromptId",
                table: "ChatGptConversations");
        }
    }
}
