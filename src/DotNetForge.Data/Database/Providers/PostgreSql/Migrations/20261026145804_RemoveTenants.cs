using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetForge.Data.Database.Providers.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTenants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Settings keys become unique: a global row (no tenant) and a tenant row with the same key would
            // clash, so the tenant's value - the one the Settings screen showed - wins.
            migrationBuilder.Sql("DELETE FROM \"Settings\" WHERE \"TenantId\" IS NULL AND \"Key\" IN (SELECT \"Key\" FROM \"Settings\" WHERE \"TenantId\" IS NOT NULL);");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Users_TenantId_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Settings_TenantId_Key",
                table: "Settings");

            migrationBuilder.DropIndex(
                name: "IX_Roles_TenantId_Name",
                table: "Roles");

            migrationBuilder.DropIndex(
                name: "IX_PageUserPermissions_TenantId",
                table: "PageUserPermissions");

            migrationBuilder.DropIndex(
                name: "IX_Pages_TenantId_ParentPageId_Slug",
                table: "Pages");

            migrationBuilder.DropIndex(
                name: "IX_PagePermissions_TenantId",
                table: "PagePermissions");

            migrationBuilder.DropIndex(
                name: "IX_MediaFiles_TenantId",
                table: "MediaFiles");

            migrationBuilder.DropIndex(
                name: "IX_CustomEndpoints_TenantId_Method_Path",
                table: "CustomEndpoints");

            migrationBuilder.DropIndex(
                name: "IX_ContentTypes_TenantId_Slug",
                table: "ContentTypes");

            migrationBuilder.DropIndex(
                name: "IX_ContentEntries_TenantId_ContentTypeId",
                table: "ContentEntries");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_TenantId_Action",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_ApiTokens_TenantId_Name",
                table: "ApiTokens");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Webhooks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PageUserPermissions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PagePermissions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "CustomEndpoints");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ContentTypes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ContentEntryValues");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ContentEntries");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ApiTokens");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Settings_Key",
                table: "Settings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pages_ParentPageId_Slug",
                table: "Pages",
                columns: new[] { "ParentPageId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomEndpoints_Method_Path",
                table: "CustomEndpoints",
                columns: new[] { "Method", "Path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentTypes_Slug",
                table: "ContentTypes",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action",
                table: "AuditLogs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_ApiTokens_Name",
                table: "ApiTokens",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Settings_Key",
                table: "Settings");

            migrationBuilder.DropIndex(
                name: "IX_Roles_Name",
                table: "Roles");

            migrationBuilder.DropIndex(
                name: "IX_Pages_ParentPageId_Slug",
                table: "Pages");

            migrationBuilder.DropIndex(
                name: "IX_CustomEndpoints_Method_Path",
                table: "CustomEndpoints");

            migrationBuilder.DropIndex(
                name: "IX_ContentTypes_Slug",
                table: "ContentTypes");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Action",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_ApiTokens_Name",
                table: "ApiTokens");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Webhooks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Users",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Roles",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "PageUserPermissions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Pages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "PagePermissions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "MediaFiles",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "CustomEndpoints",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ContentTypes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ContentEntryValues",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ContentEntries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AuditLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ApiTokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DefaultLocale = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DefaultTheme = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PathPrefix = table.Column<string>(type: "text", nullable: true),
                    PrimaryDomain = table.Column<string>(type: "text", nullable: true),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Subdomains = table.Column<string>(type: "text", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_Email",
                table: "Users",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Settings_TenantId_Key",
                table: "Settings",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_TenantId_Name",
                table: "Roles",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PageUserPermissions_TenantId",
                table: "PageUserPermissions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Pages_TenantId_ParentPageId_Slug",
                table: "Pages",
                columns: new[] { "TenantId", "ParentPageId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PagePermissions_TenantId",
                table: "PagePermissions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_TenantId",
                table: "MediaFiles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomEndpoints_TenantId_Method_Path",
                table: "CustomEndpoints",
                columns: new[] { "TenantId", "Method", "Path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentTypes_TenantId_Slug",
                table: "ContentTypes",
                columns: new[] { "TenantId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentEntries_TenantId_ContentTypeId",
                table: "ContentEntries",
                columns: new[] { "TenantId", "ContentTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_Action",
                table: "AuditLogs",
                columns: new[] { "TenantId", "Action" });

            migrationBuilder.CreateIndex(
                name: "IX_ApiTokens_TenantId_Name",
                table: "ApiTokens",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true);
        }
    }
}
