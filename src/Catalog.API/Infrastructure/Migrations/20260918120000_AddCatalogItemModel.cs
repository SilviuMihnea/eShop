using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eShop.Catalog.API.Infrastructure.Migrations;

[DbContext(typeof(CatalogContext))]
[Migration("20260918120000_AddCatalogItemModel")]
public partial class AddCatalogItemModel : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Model",
            table: "Catalog",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Catalog_Model",
            table: "Catalog",
            column: "Model");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Catalog_Model",
            table: "Catalog");

        migrationBuilder.DropColumn(
            name: "Model",
            table: "Catalog");
    }
}
