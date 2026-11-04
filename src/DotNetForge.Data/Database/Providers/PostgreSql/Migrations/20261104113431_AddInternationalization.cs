using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetForge.Data.Database.Providers.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddInternationalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdminLanguage",
                table: "Users",
                type: "character varying(35)",
                maxLength: 35,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Localized",
                table: "ContentTypes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ContentEntryTranslations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    DataJson = table.Column<string>(type: "text", nullable: false),
                    PublishedDataJson = table.Column<string>(type: "text", nullable: true),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentEntryTranslations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Locales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locales", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PageTranslations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PageId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    MetaTitle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    MetaDescription = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LayoutJson = table.Column<string>(type: "text", nullable: true),
                    DraftLayoutJson = table.Column<string>(type: "text", nullable: true),
                    DraftVersion = table.Column<Guid>(type: "uuid", nullable: true),
                    DraftUpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DraftUpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    LayoutPublishedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageTranslations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentEntryTranslations_EntryId_LocaleId",
                table: "ContentEntryTranslations",
                columns: new[] { "EntryId", "LocaleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentEntryTranslations_LocaleId",
                table: "ContentEntryTranslations",
                column: "LocaleId");

            migrationBuilder.CreateIndex(
                name: "IX_Locales_Code",
                table: "Locales",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Locales_IsDefault",
                table: "Locales",
                column: "IsDefault",
                unique: true,
                filter: "\"IsDefault\"");

            migrationBuilder.CreateIndex(
                name: "IX_PageTranslations_LocaleId",
                table: "PageTranslations",
                column: "LocaleId");

            migrationBuilder.CreateIndex(
                name: "IX_PageTranslations_PageId_LocaleId",
                table: "PageTranslations",
                columns: new[] { "PageId", "LocaleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentEntryTranslations");

            migrationBuilder.DropTable(
                name: "Locales");

            migrationBuilder.DropTable(
                name: "PageTranslations");

            migrationBuilder.DropColumn(
                name: "AdminLanguage",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Localized",
                table: "ContentTypes");
        }
    }
}
