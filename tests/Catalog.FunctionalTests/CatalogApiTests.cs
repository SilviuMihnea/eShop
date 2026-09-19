using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Asp.Versioning;
using Asp.Versioning.Http;
using eShop.Catalog.API.Model;
using Microsoft.AspNetCore.Mvc.Testing;

namespace eShop.Catalog.FunctionalTests;

public sealed class CatalogApiTests : IClassFixture<CatalogApiFixture>
{
    private readonly WebApplicationFactory<Program> _webApplicationFactory;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new(JsonSerializerDefaults.Web);

    public CatalogApiTests(CatalogApiFixture fixture)
    {
        _webApplicationFactory = fixture;
    }

    private HttpClient CreateHttpClient(ApiVersion apiVersion)
    {
        var handler = new ApiVersionHandler(new QueryStringApiVersionWriter(), apiVersion);
        return _webApplicationFactory.CreateDefaultClient(handler);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task GetCatalogItemsRespectsPageSize(double version)
    {
        var _httpClient = CreateHttpClient(new ApiVersion(version));

        // Act
        var response = await _httpClient.GetAsync("/api/catalog/items?pageIndex=0&pageSize=5", TestContext.Current.CancellationToken);

        // Assert
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<PaginatedItems<CatalogItem>>(body, _jsonSerializerOptions);

        Assert.Equal(5, result.Data.Count());
        Assert.Equal(0, result.PageIndex);
        Assert.Equal(5, result.PageSize);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task UpdateCatalogItemWorksWithoutPriceUpdate(double version)
    {
        var _httpClient = CreateHttpClient(new ApiVersion(version));

        // Act - 1
        var response = await _httpClient.GetAsync("/api/catalog/items/1", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var itemToUpdate = JsonSerializer.Deserialize<CatalogItem>(body, _jsonSerializerOptions);

        // Act - 2
        var priorAvailableStock = itemToUpdate.AvailableStock;
        itemToUpdate.AvailableStock -= 1;
        response = version switch
        {
            1.0 => await _httpClient.PutAsJsonAsync("/api/catalog/items", itemToUpdate, TestContext.Current.CancellationToken),
            2.0 => await _httpClient.PutAsJsonAsync($"/api/catalog/items/{itemToUpdate.Id}", itemToUpdate, TestContext.Current.CancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, null)
        };
        response.EnsureSuccessStatusCode();

        // Act - 3
        response = await _httpClient.GetAsync("/api/catalog/items/1", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var updatedItem = JsonSerializer.Deserialize<CatalogItem>(body, _jsonSerializerOptions);

        // Assert - 1
        Assert.Equal(itemToUpdate.Id, updatedItem.Id);
        Assert.NotEqual(priorAvailableStock, updatedItem.AvailableStock);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task UpdateCatalogItemWorksWithPriceUpdate(double version)
    {
        var _httpClient = CreateHttpClient(new ApiVersion(version));

        // Act - 1
        var response = await _httpClient.GetAsync("/api/catalog/items/1", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var itemToUpdate = JsonSerializer.Deserialize<CatalogItem>(body, _jsonSerializerOptions);

        // Act - 2
        var priorAvailableStock = itemToUpdate.AvailableStock;
        itemToUpdate.AvailableStock -= 1;
        itemToUpdate.Price = 1.99m;
        response = version switch
        {
            1.0 => await _httpClient.PutAsJsonAsync("/api/catalog/items", itemToUpdate, TestContext.Current.CancellationToken),
            2.0 => await _httpClient.PutAsJsonAsync($"/api/catalog/items/{itemToUpdate.Id}", itemToUpdate, TestContext.Current.CancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, null)
        };
        response.EnsureSuccessStatusCode();

        // Act - 3
        response = await _httpClient.GetAsync("/api/catalog/items/1", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var updatedItem = JsonSerializer.Deserialize<CatalogItem>(body, _jsonSerializerOptions);

        // Assert - 1
        Assert.Equal(itemToUpdate.Id, updatedItem.Id);
        Assert.Equal(1.99m, updatedItem.Price);
        Assert.NotEqual(priorAvailableStock, updatedItem.AvailableStock);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task GetCatalogItemsbyIds(double version)
    {
        var _httpClient = CreateHttpClient(new ApiVersion(version));

        // Act
        var response = await _httpClient.GetAsync("/api/catalog/items/by?ids=1&ids=2&ids=3", TestContext.Current.CancellationToken);

        // Arrange
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<List<CatalogItem>>(body, _jsonSerializerOptions);

        // Assert 3 items
        Assert.Equal(3, result.Count);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task GetCatalogItemWithId(double version)
    {
        var _httpClient = CreateHttpClient(new ApiVersion(version));

        // Act
        var response = await _httpClient.GetAsync("/api/catalog/items/2", TestContext.Current.CancellationToken);

        // Arrange
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<CatalogItem>(body, _jsonSerializerOptions);

        // Assert
        Assert.Equal(2, result.Id);
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task GetCatalogItemWithExactName(double version)
    {
        var _httpClient = CreateHttpClient(new ApiVersion(version));

        // Act
        var response = version switch
        {
            1.0 => await _httpClient.GetAsync("api/catalog/items/by/Wanderer%20Black%20Hiking%20Boots?PageSize=5&PageIndex=0", TestContext.Current.CancellationToken),
            2.0 => await _httpClient.GetAsync("api/catalog/items?name=Wanderer%20Black%20Hiking%20Boots&PageSize=5&PageIndex=0", TestContext.Current.CancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, null)
        };

        // Arrange
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<PaginatedItems<CatalogItem>>(body, _jsonSerializerOptions);

        // Assert
        Assert.NotNull(result.Data);
        Assert.Equal(1, result.Count);
        Assert.Equal(0, result.PageIndex);
        Assert.Equal(5, result.PageSize);
        Assert.Equal("Wanderer Black Hiking Boots", result.Data.ToList().FirstOrDefault().Name);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task GetCatalogItemWithPartialName(double version)
    {
        var _httpClient = CreateHttpClient(new ApiVersion(version));

        // Act
        var response = version switch
        {
            1.0 => await _httpClient.GetAsync("api/catalog/items/by/Alpine?PageSize=5&PageIndex=0", TestContext.Current.CancellationToken),
            2.0 => await _httpClient.GetAsync("api/catalog/items?name=Alpine&PageSize=5&PageIndex=0", TestContext.Current.CancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, null)
        };

        // Arrange
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<PaginatedItems<CatalogItem>>(body, _jsonSerializerOptions);

        // Assert
        Assert.NotNull(result.Data);
        Assert.Equal(4, result.Count);
        Assert.Equal(0, result.PageIndex);
        Assert.Equal(5, result.PageSize);
        Assert.Contains("Alpine", result.Data.ToList().FirstOrDefault().Name);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task GetCatalogItemPicWithId(double version)
    {
        var _httpClient = CreateHttpClient(new ApiVersion(version));

        // Act
        var response = await _httpClient.GetAsync("api/catalog/items/1/pic", TestContext.Current.CancellationToken);

        // Arrange
        response.EnsureSuccessStatusCode();
        var result = response.Content.Headers.ContentType.MediaType;

        // Assert
        Assert.Equal("image/webp", result);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task GetCatalogItemWithsemanticrelevance(double version)
    {
        var _httpClient = CreateHttpClient(new ApiVersion(version));

        // Act
        var response = version switch
        {
            1.0 => await _httpClient.GetAsync("api/catalog/items/withsemanticrelevance/Wanderer?PageSize=5&PageIndex=0", TestContext.Current.CancellationToken),
            2.0 => await _httpClient.GetAsync("api/catalog/items/withsemanticrelevance?text=Wanderer&PageSize=5&PageIndex=0", TestContext.Current.CancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, null)
        };

        // Arrange
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<PaginatedItems<CatalogItem>>(body, _jsonSerializerOptions);

        // Assert
        Assert.Equal(1, result.Count);
        Assert.NotNull(result.Data);
        Assert.Equal(0, result.PageIndex);
        Assert.Equal(5, result.PageSize);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task GetCatalogItemWithTypeIdBrandId(double version)
    {
        var _httpClient = CreateHttpClient(new ApiVersion(version));

        // Act
        var response = version switch
        {
            1.0 => await _httpClient.GetAsync("api/catalog/items/type/3/brand/3?PageSize=5&PageIndex=0", TestContext.Current.CancellationToken),
            2.0 => await _httpClient.GetAsync("api/catalog/items?type=3&brand=3&PageSize=5&PageIndex=0", TestContext.Current.CancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, null)
        };

        // Arrange
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<PaginatedItems<CatalogItem>>(body, _jsonSerializerOptions);

        // Assert
        Assert.NotNull(result.Data);
        Assert.Equal(4, result.Count);
        Assert.Equal(0, result.PageIndex);
        Assert.Equal(5, result.PageSize);
        Assert.Equal(3, result.Data.ToList().FirstOrDefault().CatalogTypeId);
        Assert.Equal(3, result.Data.ToList().FirstOrDefault().CatalogBrandId);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task GetAllCatalogTypeItemWithBrandId(double version)
    {
        var _httpClient = CreateHttpClient(new ApiVersion(version));

        // Act
        var response = version switch
        {
            1.0 => await _httpClient.GetAsync("api/catalog/items/type/all/brand/3?PageSize=5&PageIndex=0", TestContext.Current.CancellationToken),
            2.0 => await _httpClient.GetAsync("api/catalog/items?brand=3&PageSize=5&PageIndex=0", TestContext.Current.CancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, null)
        };

        // Arrange
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<PaginatedItems<CatalogItem>>(body, _jsonSerializerOptions);

        // Assert
        Assert.NotNull(result.Data);
        Assert.Equal(11, result.Count);
        Assert.Equal(0, result.PageIndex);
        Assert.Equal(5, result.PageSize);
        Assert.Equal(3, result.Data.ToList().FirstOrDefault().CatalogBrandId);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task GetAllCatalogTypes(double version)
    {
        var _httpClient = CreateHttpClient(new ApiVersion(version));

        // Act
        var response = await _httpClient.GetAsync("api/catalog/catalogtypes", TestContext.Current.CancellationToken);

        // Arrange
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<List<CatalogType>>(body, _jsonSerializerOptions);

        // Assert
        Assert.Equal(8, result.Count);
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task GetAllCatalogBrands(double version)
    {
        var _httpClient = CreateHttpClient(new ApiVersion(version));

        // Act
        var response = await _httpClient.GetAsync("api/catalog/catalogbrands", TestContext.Current.CancellationToken);

        // Arrange
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<List<CatalogBrand>>(body, _jsonSerializerOptions);

        // Assert
        Assert.Equal(13, result.Count);
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task AddCatalogItem(double version)
    {
        var _httpClient = CreateHttpClient(new ApiVersion(version));

        var id = version switch {
            1.0 => 10015,
            2.0 => 10016,
            _ => 0
        };

        // Act - 1
        var bodyContent = new CatalogItem("TestCatalog1") {
            Id = id,
            Model = "TestModel1",
            Description = "Test catalog description 1",
            Price = 11000.08m,
            PictureFileName = null,
            CatalogTypeId = 8,
            CatalogType = null,
            CatalogBrandId = 13,
            CatalogBrand = null,
            AvailableStock = 100,
            RestockThreshold = 10,
            MaxStockThreshold = 200,
            OnReorder = false
        };
        var response = await _httpClient.PostAsJsonAsync("/api/catalog/items", bodyContent, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        // Act - 2
        response = await _httpClient.GetAsync($"/api/catalog/items/{id}", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var addedItem = JsonSerializer.Deserialize<CatalogItem>(body, _jsonSerializerOptions);

        // Assert - 1
        Assert.Equal(bodyContent.Id, addedItem.Id);
        Assert.Equal(bodyContent.Model, addedItem.Model);

        response = version switch
        {
            1.0 => await _httpClient.GetAsync("/api/catalog/items/by/TestModel1?pageIndex=0&pageSize=5", TestContext.Current.CancellationToken),
            2.0 => await _httpClient.GetAsync("/api/catalog/items?name=TestModel1&pageIndex=0&pageSize=5", TestContext.Current.CancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, null)
        };
        response.EnsureSuccessStatusCode();
        var matchingItems = await response.Content.ReadFromJsonAsync<PaginatedItems<CatalogItem>>(_jsonSerializerOptions, TestContext.Current.CancellationToken);
        Assert.Contains(matchingItems.Data, item => item.Id == id && item.Model == bodyContent.Model);

    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task DeleteCatalogItem(double version)
    {
        var _httpClient = CreateHttpClient(new ApiVersion(version));

        var id = version switch {
            1.0 => 5,
            2.0 => 6,
            _ => 0
        };

        //Act - 1
        var response = await _httpClient.DeleteAsync($"/api/catalog/items/{id}", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        // Act - 2
        var response1 = await _httpClient.GetAsync($"/api/catalog/items/{id}", TestContext.Current.CancellationToken);
        var responseStatus = response1.StatusCode;

        // Assert - 1
        Assert.Equal("NoContent", response.StatusCode.ToString());
        Assert.Equal("NotFound", responseStatus.ToString());
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task GetCatalogItemRejectsInvalidId(double version)
    {
        var httpClient = CreateHttpClient(new ApiVersion(version));

        var response = await httpClient.GetAsync("/api/catalog/items/0", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task GetAndDeleteMissingCatalogItemReturnNotFound(double version)
    {
        var httpClient = CreateHttpClient(new ApiVersion(version));
        const int missingId = int.MaxValue;

        var getResponse = await httpClient.GetAsync($"/api/catalog/items/{missingId}", TestContext.Current.CancellationToken);
        var deleteResponse = await httpClient.DeleteAsync($"/api/catalog/items/{missingId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task CatalogPaginationBeyondLastPageReturnsEmptyData(double version)
    {
        var httpClient = CreateHttpClient(new ApiVersion(version));

        var response = await httpClient.GetAsync(
            "/api/catalog/items?pageIndex=1000&pageSize=10",
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PaginatedItems<CatalogItem>>(
            _jsonSerializerOptions,
            TestContext.Current.CancellationToken);
        Assert.Empty(result!.Data);
        Assert.Equal(1000, result.PageIndex);
    }

    // The model filter is a v2-only query parameter: the v1 /items route takes no filters.
    [Fact]
    public async Task GetCatalogItemsFilteredByModel()
    {
        var httpClient = CreateHttpClient(new ApiVersion(2.0));

        var response = await httpClient.GetAsync(
            "/api/catalog/items?model=Everest+Line+3&pageIndex=0&pageSize=10",
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PaginatedItems<CatalogItem>>(
            _jsonSerializerOptions,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.All(result.Data, item => Assert.Equal("Everest Line 3", item.Model));
    }

    [Fact]
    public async Task GetCatalogItemsFilteredByMultipleModelsUnionsThem()
    {
        var httpClient = CreateHttpClient(new ApiVersion(2.0));

        var response = await httpClient.GetAsync(
            "/api/catalog/items?model=Everest+Line+3&model=Apex+AH-7&pageIndex=0&pageSize=10",
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PaginatedItems<CatalogItem>>(
            _jsonSerializerOptions,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(4, result.Count);
        Assert.All(result.Data, item => Assert.Contains(item.Model, new[] { "Everest Line 3", "Apex AH-7" }));
    }

    [Fact]
    public async Task GetCatalogFacetsScopesModelsToSelectedBrand()
    {
        var httpClient = CreateHttpClient(new ApiVersion(2.0));

        var brands = await httpClient.GetFromJsonAsync<CatalogBrand[]>(
            "/api/catalog/catalogbrands",
            _jsonSerializerOptions,
            TestContext.Current.CancellationToken);
        var brandId = brands!.Single(b => b.Brand == "Legend").Id;

        var items = await httpClient.GetFromJsonAsync<PaginatedItems<CatalogItem>>(
            $"/api/catalog/items?brand={brandId}&pageIndex=0&pageSize=100",
            _jsonSerializerOptions,
            TestContext.Current.CancellationToken);

        var expected = items!.Data
            .Where(i => i.Model is not null)
            .GroupBy(i => i.Model!)
            .Select(g => (Model: g.Key, Count: g.Count()))
            .OrderBy(x => x.Model)
            .ToList();

        var facets = await httpClient.GetFromJsonAsync<CatalogFacets>(
            $"/api/catalog/items/facets?brand={brandId}",
            _jsonSerializerOptions,
            TestContext.Current.CancellationToken);

        var actual = facets!.ModelCounts
            .Select(m => (m.Model, m.Count))
            .OrderBy(x => x.Model)
            .ToList();

        // The model options offered for a brand are exactly that brand's models.
        Assert.Equal(expected, actual);

        // Clearing the model filter brings back the brand's items that have no model at
        // all, so the total is every item in scope rather than the sum of the counts.
        Assert.Equal(items.Count, (long)facets.ModelTotal);
        Assert.True(facets.ModelTotal > actual.Sum(x => x.Count));
    }

    [Fact]
    public async Task GetCatalogFacetsScopesBrandsToSelectedModel()
    {
        var httpClient = CreateHttpClient(new ApiVersion(2.0));

        var brands = await httpClient.GetFromJsonAsync<CatalogBrand[]>(
            "/api/catalog/catalogbrands",
            _jsonSerializerOptions,
            TestContext.Current.CancellationToken);
        var brandId = brands!.Single(b => b.Brand == "Legend").Id;

        var facets = await httpClient.GetFromJsonAsync<CatalogFacets>(
            "/api/catalog/items/facets?model=Everest+Line+3",
            _jsonSerializerOptions,
            TestContext.Current.CancellationToken);

        // Counts for the other facets intersect the active model selection.
        var brandCount = Assert.Single(facets!.BrandCounts);
        Assert.Equal(brandId, brandCount.Id);
        Assert.Equal(3, brandCount.Count);
    }
}
