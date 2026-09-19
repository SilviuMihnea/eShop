using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eShop.Catalog.API.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddInventoryReservation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "ReservedStock",
            table: "Catalog",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        // xmin is a PostgreSQL system column. UseXminAsConcurrencyToken maps it; it must not
        // be added as a user column.

        migrationBuilder.CreateTable(
            name: "Reservation",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                OrderId = table.Column<int>(type: "integer", nullable: false),
                ProductId = table.Column<int>(type: "integer", nullable: false),
                Units = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ReservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CommittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ReleasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Reservation", x => x.Id);
                table.ForeignKey(
                    name: "FK_Reservation_Catalog_ProductId",
                    column: x => x.ProductId,
                    principalTable: "Catalog",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Reservation_OrderId",
            table: "Reservation",
            column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_Reservation_OrderId_ProductId",
            table: "Reservation",
            columns: new[] { "OrderId", "ProductId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Reservation_ProductId",
            table: "Reservation",
            column: "ProductId");

        migrationBuilder.CreateIndex(
            name: "IX_Reservation_Status_ExpiresAt",
            table: "Reservation",
            columns: new[] { "Status", "ExpiresAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Reservation");

        migrationBuilder.DropColumn(
            name: "ReservedStock",
            table: "Catalog");
    }
}
