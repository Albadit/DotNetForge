using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetForge.Data.Database.Providers.PostgreSql.Migrations
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
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ContentEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SearchText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DataJson = table.Column<string>(type: "text", nullable: false),
                    PublishedDataJson = table.Column<string>(type: "text", nullable: true),
                    Published = table.Column<bool>(type: "boolean", nullable: false),
                    HasDraft = table.Column<bool>(type: "boolean", nullable: false),
                    PublishedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishedById = table.Column<Guid>(type: "uuid", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentEntryValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Published = table.Column<bool>(type: "boolean", nullable: false),
                    Field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Number = table.Column<double>(type: "double precision", nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentEntryValues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Icon = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    FieldsJson = table.Column<string>(type: "text", nullable: false),
                    TitleField = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Drafts = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentTypes", x => x.Id);
                });

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
