namespace eShop.Catalog.API.Infrastructure.EntityConfigurations;

class CatalogItemEntityTypeConfiguration
    : IEntityTypeConfiguration<CatalogItem>
{
    public void Configure(EntityTypeBuilder<CatalogItem> builder)
    {
        builder.ToTable("Catalog");

        // Postgres advances xmin on every row update, so treating it as the concurrency token
        // makes a stock read-modify-write fail loudly instead of silently overwriting a
        // reservation taken by a concurrent order. Reserving reads available-to-promise and
        // writes it back in a later transaction, so without this two orders can both be told
        // the last unit is theirs.
        builder.UseXminAsConcurrencyToken();

        builder.Property(ci => ci.Name)
            .HasMaxLength(50);

        builder.Property(ci => ci.Model)
            .HasMaxLength(100);

        builder.Property(ci => ci.Embedding)
            .HasColumnType("vector(384)");

        builder.HasOne(ci => ci.CatalogBrand)
            .WithMany();

        builder.HasOne(ci => ci.CatalogType)
            .WithMany();

        builder.HasIndex(ci => ci.Name);
        builder.HasIndex(ci => ci.Model);
    }
}
