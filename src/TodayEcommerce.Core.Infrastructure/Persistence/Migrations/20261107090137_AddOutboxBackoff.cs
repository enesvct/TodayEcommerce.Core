using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodayEcommerce.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxBackoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "next_attempt_on_utc",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "next_attempt_on_utc",
                table: "outbox_messages");
        }
    }
}
