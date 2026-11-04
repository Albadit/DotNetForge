using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetForge.Data.Database.Providers.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddEnvironmentVariables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Settings_Key",
                table: "Settings");

            migrationBuilder.AlterColumn<string>(
                name: "Value",
                table: "Settings",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedDate",
                table: "Settings",
                type: "timestamp with time zone",
                nullable: false,
                // Existing settings: dated when the migration runs.
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "Environment",
                table: "Settings",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsSecret",
                table: "Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "Settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedById",
                table: "Settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedDate",
                table: "Settings",
                type: "timestamp with time zone",
                nullable: false,
                // Existing settings: dated when the migration runs.
                defaultValueSql: "now()");

            migrationBuilder.CreateIndex(
                name: "IX_Settings_Key_Environment",
                table: "Settings",
                columns: new[] { "Key", "Environment" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Settings_Key_Environment",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "CreatedDate",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Environment",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "IsSecret",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "UpdatedById",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "UpdatedDate",
                table: "Settings");

            migrationBuilder.AlterColumn<string>(
                name: "Value",
                table: "Settings",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(8000)",
                oldMaxLength: 8000,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Settings_Key",
                table: "Settings",
                column: "Key",
                unique: true);
        }
    }
}
