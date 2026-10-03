using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechBazar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase4Indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductSpecifications_Key_Value",
                table: "ProductSpecifications");

            migrationBuilder.DropIndex(
                name: "IX_Products_StockStatus",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CreatedAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_Status",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_OrderItems_OrderId",
                table: "OrderItems");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSpecifications_Key_Value",
                table: "ProductSpecifications",
                columns: new[] { "Key", "Value" })
                .Annotation("SqlServer:Include", new[] { "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_StockStatus_StockQuantity",
                table: "Products",
                columns: new[] { "StockStatus", "StockQuantity" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedAt",
                table: "Orders",
                column: "CreatedAt")
                .Annotation("SqlServer:Include", new[] { "Status", "GrandTotal" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status_CreatedAt",
                table: "Orders",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId",
                table: "OrderItems",
                column: "OrderId")
                .Annotation("SqlServer:Include", new[] { "ProductId", "Quantity", "LineTotal" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductSpecifications_Key_Value",
                table: "ProductSpecifications");

            migrationBuilder.DropIndex(
                name: "IX_Products_StockStatus_StockQuantity",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CreatedAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_Status_CreatedAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_OrderItems_OrderId",
                table: "OrderItems");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSpecifications_Key_Value",
                table: "ProductSpecifications",
                columns: new[] { "Key", "Value" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_StockStatus",
                table: "Products",
                column: "StockStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedAt",
                table: "Orders",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status",
                table: "Orders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId",
                table: "OrderItems",
                column: "OrderId");
        }
    }
}
