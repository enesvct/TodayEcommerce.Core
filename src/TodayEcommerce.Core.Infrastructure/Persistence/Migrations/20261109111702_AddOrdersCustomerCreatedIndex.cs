using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodayEcommerce.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrdersCustomerCreatedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_customer_id",
                table: "orders");

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer_id_created_at_utc",
                table: "orders",
                columns: new[] { "customer_id", "created_at_utc" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_customer_id_created_at_utc",
                table: "orders");

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer_id",
                table: "orders",
                column: "customer_id");
        }
    }
}
