using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souqly.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LowStockThreshold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "low_stock_threshold",
                table: "stock_items",
                type: "integer",
                nullable: false,
                defaultValue: 3);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "low_stock_threshold",
                table: "stock_items");
        }
    }
}
