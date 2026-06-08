using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetForge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPageSeoAndScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CanonicalUrl",
                table: "Pages",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileReference",
                table: "Pages",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledPublishDate",
                table: "Pages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledUnpublishDate",
                table: "Pages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeoKeywords",
                table: "Pages",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanonicalUrl",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "FileReference",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "ScheduledPublishDate",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "ScheduledUnpublishDate",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "SeoKeywords",
                table: "Pages");
        }
    }
}
