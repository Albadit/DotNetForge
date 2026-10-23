using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetForge.Data.Database.Providers.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddPageBuilderAndPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DraftLayoutJson",
                table: "Pages",
                type: "longtext",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DraftUpdatedById",
                table: "Pages",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DraftUpdatedDate",
                table: "Pages",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DraftVersion",
                table: "Pages",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LayoutJson",
                table: "Pages",
                type: "longtext",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LayoutPublishedDate",
                table: "Pages",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NoIndex",
                table: "Pages",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SocialImageUrl",
                table: "Pages",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThemeId",
                table: "Pages",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PagePermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PageId = table.Column<Guid>(type: "char(36)", nullable: false),
                    RoleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Permission = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PagePermissions", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PagePermissions_PageId_RoleId_Permission",
                table: "PagePermissions",
                columns: new[] { "PageId", "RoleId", "Permission" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PagePermissions_TenantId",
                table: "PagePermissions",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PagePermissions");

            migrationBuilder.DropColumn(
                name: "DraftLayoutJson",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "DraftUpdatedById",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "DraftUpdatedDate",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "DraftVersion",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "LayoutJson",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "LayoutPublishedDate",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "NoIndex",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "SocialImageUrl",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "ThemeId",
                table: "Pages");
        }
    }
}
