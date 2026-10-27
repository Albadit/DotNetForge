using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetForge.Data.Database.Providers.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FolderPath",
                table: "MediaFiles");

            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "MediaFiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MediaFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaFolders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_FolderId",
                table: "MediaFiles",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFolders_ParentId",
                table: "MediaFolders",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFolders_Path",
                table: "MediaFolders",
                column: "Path",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaFolders");

            migrationBuilder.DropIndex(
                name: "IX_MediaFiles_FolderId",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "MediaFiles");

            migrationBuilder.AddColumn<string>(
                name: "FolderPath",
                table: "MediaFiles",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");
        }
    }
}
