using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetForge.Data.Database.Providers.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddSignInProvidersAndMfa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "SettingsJson",
                table: "AuthProviders",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000);

            migrationBuilder.AddColumn<string>(
                name: "Key",
                table: "AuthProviders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedDate",
                table: "AuthProviders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "UserMfaMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Secret = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CredentialId = table.Column<byte[]>(type: "bytea", maxLength: 1024, nullable: true),
                    PublicKey = table.Column<byte[]>(type: "bytea", maxLength: 2048, nullable: true),
                    Counter = table.Column<long>(type: "bigint", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMfaMethods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserMfaMethods_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRecoveryCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRecoveryCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRecoveryCodes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // The seeded rows get their catalog key ("Twitter/X" -> "x", "GitHub" -> "github") before the unique index;
            // DataSeeder then adds the missing catalog providers and drops the never-configured old ones.
            migrationBuilder.Sql("""
                UPDATE "AuthProviders"
                SET "Key" = CASE "Name" WHEN 'Twitter/X' THEN 'x' ELSE lower(regexp_replace("Name", '[^A-Za-z0-9]', '', 'g')) END,
                    "UpdatedDate" = now() AT TIME ZONE 'utc';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AuthProviders_Key",
                table: "AuthProviders",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserMfaMethods_CredentialId",
                table: "UserMfaMethods",
                column: "CredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMfaMethods_UserId",
                table: "UserMfaMethods",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRecoveryCodes_UserId",
                table: "UserRecoveryCodes",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserMfaMethods");

            migrationBuilder.DropTable(
                name: "UserRecoveryCodes");

            migrationBuilder.DropIndex(
                name: "IX_AuthProviders_Key",
                table: "AuthProviders");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "AuthProviders");

            migrationBuilder.DropColumn(
                name: "UpdatedDate",
                table: "AuthProviders");

            migrationBuilder.AlterColumn<string>(
                name: "SettingsJson",
                table: "AuthProviders",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(8000)",
                oldMaxLength: 8000);
        }
    }
}
