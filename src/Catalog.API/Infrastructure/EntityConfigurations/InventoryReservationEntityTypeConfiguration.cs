namespace eShop.Catalog.API.Infrastructure.EntityConfigurations;

class InventoryReservationEntityTypeConfiguration
    : IEntityTypeConfiguration<InventoryReservation>
{
    public void Configure(EntityTypeBuilder<InventoryReservation> builder)
    {
        builder.ToTable("Reservation");

        // Stored as text so the rows stay readable, matching how the ordering database stores
        // OrderStatus.
        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        // No navigation property on CatalogItem: that type is also the catalog API's response
        // body, and a reservations collection would leak into it.
        builder.HasOne<CatalogItem>()
            .WithMany()
            .HasForeignKey(r => r.ProductId);

        // At most one reservation per order line. This is the constraint that makes the
        // reserve handler idempotent under at-least-once delivery.
        builder.HasIndex(r => new { r.OrderId, r.ProductId })
            .IsUnique();

        // Settling an order looks its reservations up by order.
        builder.HasIndex(r => r.OrderId);

        // The expiry sweep scans for held reservations that are past their expiry.
        builder.HasIndex(r => new { r.Status, r.ExpiresAt });
    }
}
