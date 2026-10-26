using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetForge.Data.Database.Providers.SqlServer.Migrations
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SearchText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublishedDataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Published = table.Column<bool>(type: "bit", nullable: false),
                    HasDraft = table.Column<bool>(type: "bit", nullable: false),
                    PublishedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentEntryValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Published = table.Column<bool>(type: "bit", nullable: false),
                    Field = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Number = table.Column<double>(type: "float", nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentEntryValues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Icon = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    FieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TitleField = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Drafts = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
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
