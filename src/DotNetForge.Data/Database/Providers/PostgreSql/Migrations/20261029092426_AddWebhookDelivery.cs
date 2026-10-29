using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetForge.Data.Database.Providers.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                table: "WebhookDeliveries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptDate",
                table: "WebhookDeliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestBody",
                table: "WebhookDeliveries",
                type: "character varying(65536)",
                maxLength: 65536,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseBody",
                table: "WebhookDeliveries",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_Outcome_NextAttemptDate",
                table: "WebhookDeliveries",
                columns: new[] { "Outcome", "NextAttemptDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebhookDeliveries_Outcome_NextAttemptDate",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "NextAttemptDate",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "RequestBody",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "ResponseBody",
                table: "WebhookDeliveries");
        }
    }
}
