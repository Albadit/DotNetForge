using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetForge.Data.Database.Providers.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddEndpointTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TokenProtected",
                table: "CustomEndpoints",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TokenProtected",
                table: "CustomEndpoints");
        }
    }
}
