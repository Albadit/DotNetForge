using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetForge.Data.Database.Providers.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaOptimization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Optimization",
                table: "MediaFiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "OptimizationStartedDate",
                table: "MediaFiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OriginalSizeBytes",
                table: "MediaFiles",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_Optimization",
                table: "MediaFiles",
                column: "Optimization");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaFiles_Optimization",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "Optimization",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "OptimizationStartedDate",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "OriginalSizeBytes",
                table: "MediaFiles");
        }
    }
}
