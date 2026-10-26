using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetForge.Data.Database.Providers.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddEndpointOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomEndpointOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EndpointId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Path = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Sql = table.Column<string>(type: "text", nullable: false),
                    ParametersJson = table.Column<string>(type: "text", nullable: false),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    MaxRows = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomEndpointOperations", x => x.Id);
                });

            // Each existing endpoint becomes an endpoint with one operation: its method, SQL and settings move
            // to the new table (same id, the endpoint's own path) before the old columns are dropped.
            migrationBuilder.Sql("INSERT INTO \"CustomEndpointOperations\" (\"Id\", \"EndpointId\", \"Name\", \"Method\", \"Path\", \"Sql\", \"ParametersJson\", \"Result\", \"TimeoutSeconds\", \"MaxRows\", \"SortOrder\") SELECT \"Id\", \"Id\", NULL, \"Method\", '', \"Sql\", \"ParametersJson\", \"Result\", \"TimeoutSeconds\", \"MaxRows\", 0 FROM \"CustomEndpoints\";");

            migrationBuilder.DropIndex(
                name: "IX_CustomEndpoints_Method_Path",
                table: "CustomEndpoints");

            migrationBuilder.DropColumn(
                name: "MaxRows",
                table: "CustomEndpoints");

            migrationBuilder.DropColumn(
                name: "Method",
                table: "CustomEndpoints");

            migrationBuilder.DropColumn(
                name: "ParametersJson",
                table: "CustomEndpoints");

            migrationBuilder.DropColumn(
                name: "Result",
                table: "CustomEndpoints");

            migrationBuilder.DropColumn(
                name: "Sql",
                table: "CustomEndpoints");

            migrationBuilder.DropColumn(
                name: "TimeoutSeconds",
                table: "CustomEndpoints");

            migrationBuilder.CreateIndex(
                name: "IX_CustomEndpoints_Path",
                table: "CustomEndpoints",
                column: "Path");

            migrationBuilder.CreateIndex(
                name: "IX_CustomEndpointOperations_EndpointId_Method_Path",
                table: "CustomEndpointOperations",
                columns: new[] { "EndpointId", "Method", "Path" },
                unique: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomEndpointOperations");

            migrationBuilder.DropIndex(
                name: "IX_CustomEndpoints_Path",
                table: "CustomEndpoints");

            migrationBuilder.AddColumn<int>(
                name: "MaxRows",
                table: "CustomEndpoints",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Method",
                table: "CustomEndpoints",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ParametersJson",
                table: "CustomEndpoints",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Result",
                table: "CustomEndpoints",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Sql",
                table: "CustomEndpoints",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TimeoutSeconds",
                table: "CustomEndpoints",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_CustomEndpoints_Method_Path",
                table: "CustomEndpoints",
                columns: new[] { "Method", "Path" },
                unique: true);
        }
    }
}
