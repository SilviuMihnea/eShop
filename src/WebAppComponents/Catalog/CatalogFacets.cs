namespace eShop.WebAppComponents.Catalog;

public record CatalogFacetCount(int Id, int Count);

// Models are free text on the catalog item rather than a lookup entity, so they are
// counted by value instead of by id.
public record CatalogModelFacetCount(string Model, int Count);

public record CatalogFacets(
    IReadOnlyList<CatalogFacetCount> BrandCounts,
    IReadOnlyList<CatalogFacetCount> TypeCounts,
    IReadOnlyList<CatalogModelFacetCount> ModelCounts,
    int BrandTotal,
    int TypeTotal,
    int ModelTotal);
