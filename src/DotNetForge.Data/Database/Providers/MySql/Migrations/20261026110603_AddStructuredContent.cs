using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetForge.Data.Database.Providers.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuredContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SeedVersion",
                table: "SystemState",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ContentEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ContentTypeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Title = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false),
                    SearchText = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    DataJson = table.Column<string>(type: "longtext", nullable: false),
                    PublishedDataJson = table.Column<string>(type: "longtext", nullable: true),
                    Published = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    HasDraft = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PublishedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PublishedById = table.Column<Guid>(type: "char(36)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<Guid>(type: "char(36)", nullable: false),
                    CreatedById = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "char(36)", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentEntries", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ContentEntryValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ContentTypeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EntryId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Published = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Field = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Text = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    Number = table.Column<double>(type: "double", nullable: true),
                    Date = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentEntryValues", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ContentTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    Icon = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    FieldsJson = table.Column<string>(type: "longtext", nullable: false),
                    TitleField = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    Drafts = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedById = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentTypes", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ContentEntries_ContentTypeId_SortOrder",
                table: "ContentEntries",
                columns: new[] { "ContentTypeId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentEntries_TenantId_ContentTypeId",
                table: "ContentEntries",
                columns: new[] { "TenantId", "ContentTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentEntryValues_ContentTypeId_Published_Field_Number",
                table: "ContentEntryValues",
                columns: new[] { "ContentTypeId", "Published", "Field", "Number" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentEntryValues_ContentTypeId_Published_Field_Text",
                table: "ContentEntryValues",
                columns: new[] { "ContentTypeId", "Published", "Field", "Text" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentEntryValues_EntryId",
                table: "ContentEntryValues",
                column: "EntryId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentTypes_TenantId_Slug",
                table: "ContentTypes",
                columns: new[] { "TenantId", "Slug" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentEntries");

            migrationBuilder.DropTable(
                name: "ContentEntryValues");

            migrationBuilder.DropTable(
                name: "ContentTypes");

            migrationBuilder.DropColumn(
                name: "SeedVersion",
                table: "SystemState");
        }
    }
}
