using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blazor_Bedrock.Migrations
{
    /// <inheritdoc />
    public partial class AddApiConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ServiceName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EncryptedConfiguration = table.Column<string>(type: "varchar(5000)", maxLength: 5000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiConfigurations", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ChatGptQuestionGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Order = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatGptQuestionGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatGptQuestionGroups_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ChatGptQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    GroupId = table.Column<int>(type: "int", nullable: true),
                    QuestionText = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Order = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatGptQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatGptQuestions_ChatGptQuestionGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "ChatGptQuestionGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ChatGptQuestions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ChatGptQuestionResponses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ConversationId = table.Column<int>(type: "int", nullable: false),
                    QuestionId = table.Column<int>(type: "int", nullable: true),
                    DocumentId = table.Column<int>(type: "int", nullable: true),
                    PromptId = table.Column<int>(type: "int", nullable: true),
                    Response = table.Column<string>(type: "LONGTEXT", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatGptQuestionResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatGptQuestionResponses_ChatGptConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "ChatGptConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatGptQuestionResponses_ChatGptPrompts_PromptId",
                        column: x => x.PromptId,
                        principalTable: "ChatGptPrompts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ChatGptQuestionResponses_ChatGptQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "ChatGptQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ChatGptQuestionResponses_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ApiConfigurations_ServiceName",
                table: "ApiConfigurations",
                column: "ServiceName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatGptQuestionGroups_TenantId",
                table: "ChatGptQuestionGroups",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatGptQuestionGroups_TenantId_Order",
                table: "ChatGptQuestionGroups",
                columns: new[] { "TenantId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatGptQuestionResponses_ConversationId",
                table: "ChatGptQuestionResponses",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatGptQuestionResponses_ConversationId_QuestionId_DocumentId",
                table: "ChatGptQuestionResponses",
                columns: new[] { "ConversationId", "QuestionId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatGptQuestionResponses_DocumentId",
                table: "ChatGptQuestionResponses",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatGptQuestionResponses_PromptId",
                table: "ChatGptQuestionResponses",
                column: "PromptId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatGptQuestionResponses_QuestionId",
                table: "ChatGptQuestionResponses",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatGptQuestions_GroupId",
                table: "ChatGptQuestions",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatGptQuestions_TenantId",
                table: "ChatGptQuestions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatGptQuestions_TenantId_GroupId_Order",
                table: "ChatGptQuestions",
                columns: new[] { "TenantId", "GroupId", "Order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiConfigurations");

            migrationBuilder.DropTable(
                name: "ChatGptQuestionResponses");

            migrationBuilder.DropTable(
                name: "ChatGptQuestions");

            migrationBuilder.DropTable(
                name: "ChatGptQuestionGroups");
        }
    }
}
