using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blazor_Bedrock.Migrations
{
    /// <inheritdoc />
    public partial class AddRagGroupIdToConversation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RagGroupId",
                table: "ChatGptConversations",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RagGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    TopK = table.Column<int>(type: "int", nullable: false, defaultValue: 5),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PineconeIndexName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RagGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RagGroups_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RagGroups_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RagGroupDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RagGroupId = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsIndexed = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RagGroupDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RagGroupDocuments_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RagGroupDocuments_RagGroups_RagGroupId",
                        column: x => x.RagGroupId,
                        principalTable: "RagGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ChatGptConversations_RagGroupId",
                table: "ChatGptConversations",
                column: "RagGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_RagGroupDocuments_DocumentId",
                table: "RagGroupDocuments",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_RagGroupDocuments_RagGroupId_DocumentId",
                table: "RagGroupDocuments",
                columns: new[] { "RagGroupId", "DocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RagGroups_PineconeIndexName",
                table: "RagGroups",
                column: "PineconeIndexName");

            migrationBuilder.CreateIndex(
                name: "IX_RagGroups_TenantId_UserId",
                table: "RagGroups",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_RagGroups_UserId",
                table: "RagGroups",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatGptConversations_RagGroups_RagGroupId",
                table: "ChatGptConversations",
                column: "RagGroupId",
                principalTable: "RagGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatGptConversations_RagGroups_RagGroupId",
                table: "ChatGptConversations");

            migrationBuilder.DropTable(
                name: "RagGroupDocuments");

            migrationBuilder.DropTable(
                name: "RagGroups");

            migrationBuilder.DropIndex(
                name: "IX_ChatGptConversations_RagGroupId",
                table: "ChatGptConversations");

            migrationBuilder.DropColumn(
                name: "RagGroupId",
                table: "ChatGptConversations");
        }
    }
}
