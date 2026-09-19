using eShop.Catalog.API.Infrastructure;
using eShop.Catalog.API.Model;
using eShop.Catalog.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace eShop.Catalog.FunctionalTests;

/// <summary>
/// Exercises the reservation service against a real Postgres, because the behaviour that
/// matters here — all-or-nothing holds and the concurrency token that stops two orders being
/// promised the same unit — only exists at the database level.
/// </summary>
public sealed class InventoryReservationServiceTests : IClassFixture<CatalogApiFixture>
{
    private readonly CatalogApiFixture _fixture;

    public InventoryReservationServiceTests(CatalogApiFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Runs a unit of work the way a real integration event handler does: the service mutates
    /// the tracked context and the caller saves.
    /// </summary>
    private async Task<T> InScopeAsync<T>(Func<IInventoryReservationService, CatalogContext, Task<T>> work)
    {
        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInventoryReservationService>();
        var context = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        var result = await work(service, context);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return result;
    }

    private async Task<(int AvailableStock, int ReservedStock)> GetStockAsync(int productId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var item = await context.CatalogItems.SingleAsync(i => i.Id == productId, TestContext.Current.CancellationToken);
        return (item.AvailableStock, item.ReservedStock);
    }

    private async Task<List<InventoryReservation>> GetReservationsAsync(int orderId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        return await context.InventoryReservations
            .Where(r => r.OrderId == orderId)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Forces a product to an exact physical stock level with no holds against it.</summary>
    private async Task SetStockAsync(int productId, int availableStock)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var item = await context.CatalogItems.SingleAsync(i => i.Id == productId, TestContext.Current.CancellationToken);
        item.AvailableStock = availableStock;
        item.ReservedStock = 0;
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReserveHoldsUnitsWithoutReducingPhysicalStock()
    {
        const int orderId = 9001;
        const int productId = 11;
        await SetStockAsync(productId, 20);

        var outcome = await InScopeAsync((service, _) =>
            service.ReserveAsync(orderId, [new ReservationRequest(productId, 3)], TestContext.Current.CancellationToken));

        Assert.True(outcome.Success);
        var stock = await GetStockAsync(productId);
        Assert.Equal(20, stock.AvailableStock);
        Assert.Equal(3, stock.ReservedStock);

        var reservation = Assert.Single(await GetReservationsAsync(orderId));
        Assert.Equal(ReservationStatus.Reserved, reservation.Status);
        Assert.Equal(3, reservation.Units);
        Assert.True(reservation.ExpiresAt > reservation.ReservedAt);
    }

    [Fact]
    public async Task ReserveIsAllOrNothingWhenOneLineIsShort()
    {
        const int orderId = 9002;
        const int plentiful = 12;
        const int scarce = 13;
        await SetStockAsync(plentiful, 20);
        await SetStockAsync(scarce, 1);

        var outcome = await InScopeAsync((service, _) => service.ReserveAsync(
            orderId,
            [new ReservationRequest(plentiful, 2), new ReservationRequest(scarce, 5)],
            TestContext.Current.CancellationToken));

        Assert.False(outcome.Success);
        Assert.True(outcome.Lines.Single(l => l.ProductId == plentiful).Reserved);
        Assert.False(outcome.Lines.Single(l => l.ProductId == scarce).Reserved);

        // The line that could have been satisfied must not have been held either.
        Assert.Equal(0, (await GetStockAsync(plentiful)).ReservedStock);
        Assert.Equal(0, (await GetStockAsync(scarce)).ReservedStock);
        Assert.Empty(await GetReservationsAsync(orderId));
    }

    [Fact]
    public async Task ReserveTreatsUnknownProductAsShort()
    {
        const int orderId = 9003;

        var outcome = await InScopeAsync((service, _) => service.ReserveAsync(
            orderId,
            [new ReservationRequest(int.MaxValue, 1)],
            TestContext.Current.CancellationToken));

        Assert.False(outcome.Success);
        Assert.Empty(await GetReservationsAsync(orderId));
    }

    [Fact]
    public async Task ReserveTwiceHoldsUnitsOnlyOnce()
    {
        const int orderId = 9004;
        const int productId = 14;
        await SetStockAsync(productId, 10);

        await InScopeAsync((service, _) =>
            service.ReserveAsync(orderId, [new ReservationRequest(productId, 2)], TestContext.Current.CancellationToken));

        // A redelivered OrderStatusChangedToAwaitingValidation event must not double-hold.
        var second = await InScopeAsync((service, _) =>
            service.ReserveAsync(orderId, [new ReservationRequest(productId, 2)], TestContext.Current.CancellationToken));

        Assert.True(second.Success);
        Assert.Equal(2, (await GetStockAsync(productId)).ReservedStock);
        Assert.Single(await GetReservationsAsync(orderId));
    }

    [Fact]
    public async Task CommitMovesHeldUnitsOutOfPhysicalStock()
    {
        const int orderId = 9005;
        const int productId = 15;
        await SetStockAsync(productId, 10);

        await InScopeAsync((service, _) =>
            service.ReserveAsync(orderId, [new ReservationRequest(productId, 4)], TestContext.Current.CancellationToken));

        var committed = await InScopeAsync((service, _) => service.CommitAsync(orderId, TestContext.Current.CancellationToken));

        Assert.Equal(1, committed);
        var stock = await GetStockAsync(productId);
        Assert.Equal(6, stock.AvailableStock);
        Assert.Equal(0, stock.ReservedStock);

        var reservation = Assert.Single(await GetReservationsAsync(orderId));
        Assert.Equal(ReservationStatus.Committed, reservation.Status);
        Assert.NotNull(reservation.CommittedAt);
    }

    [Fact]
    public async Task CommitTwiceIsANoOp()
    {
        const int orderId = 9006;
        const int productId = 16;
        await SetStockAsync(productId, 10);

        await InScopeAsync((service, _) =>
            service.ReserveAsync(orderId, [new ReservationRequest(productId, 3)], TestContext.Current.CancellationToken));
        await InScopeAsync((service, _) => service.CommitAsync(orderId, TestContext.Current.CancellationToken));

        var again = await InScopeAsync((service, _) => service.CommitAsync(orderId, TestContext.Current.CancellationToken));

        Assert.Equal(0, again);
        Assert.Equal(7, (await GetStockAsync(productId)).AvailableStock);
    }

    [Fact]
    public async Task ReleaseGivesUnitsBackWithoutChangingPhysicalStock()
    {
        const int orderId = 9007;
        const int productId = 17;
        await SetStockAsync(productId, 10);

        await InScopeAsync((service, _) =>
            service.ReserveAsync(orderId, [new ReservationRequest(productId, 4)], TestContext.Current.CancellationToken));

        var released = await InScopeAsync((service, _) => service.ReleaseAsync(orderId, TestContext.Current.CancellationToken));

        Assert.Equal(1, released);
        var stock = await GetStockAsync(productId);
        Assert.Equal(10, stock.AvailableStock);
        Assert.Equal(0, stock.ReservedStock);

        var reservation = Assert.Single(await GetReservationsAsync(orderId));
        Assert.Equal(ReservationStatus.Released, reservation.Status);
        Assert.NotNull(reservation.ReleasedAt);
    }

    [Fact]
    public async Task ReleaseAfterCommitDoesNotClawBackSoldStock()
    {
        const int orderId = 9008;
        const int productId = 18;
        await SetStockAsync(productId, 10);

        await InScopeAsync((service, _) =>
            service.ReserveAsync(orderId, [new ReservationRequest(productId, 2)], TestContext.Current.CancellationToken));
        await InScopeAsync((service, _) => service.CommitAsync(orderId, TestContext.Current.CancellationToken));

        // A cancellation arriving after payment must find nothing outstanding.
        var released = await InScopeAsync((service, _) => service.ReleaseAsync(orderId, TestContext.Current.CancellationToken));

        Assert.Equal(0, released);
        Assert.Equal(8, (await GetStockAsync(productId)).AvailableStock);
    }

    [Fact]
    public async Task ReleasedUnitsCanBeReservedByAnotherOrder()
    {
        const int firstOrder = 9009;
        const int secondOrder = 9010;
        const int productId = 19;
        await SetStockAsync(productId, 2);

        await InScopeAsync((service, _) =>
            service.ReserveAsync(firstOrder, [new ReservationRequest(productId, 2)], TestContext.Current.CancellationToken));

        // Everything is promised, so the second order cannot be satisfied yet.
        var blocked = await InScopeAsync((service, _) =>
            service.ReserveAsync(secondOrder, [new ReservationRequest(productId, 2)], TestContext.Current.CancellationToken));
        Assert.False(blocked.Success);

        await InScopeAsync((service, _) => service.ReleaseAsync(firstOrder, TestContext.Current.CancellationToken));

        var afterRelease = await InScopeAsync((service, _) =>
            service.ReserveAsync(secondOrder, [new ReservationRequest(productId, 2)], TestContext.Current.CancellationToken));

        Assert.True(afterRelease.Success);
        Assert.Equal(2, (await GetStockAsync(productId)).ReservedStock);
    }

    /// <summary>
    /// The reason CatalogItem carries a concurrency token. Two orders both read the last unit as
    /// available and both try to hold it; the second save must fail rather than oversell.
    /// </summary>
    [Fact]
    public async Task ConcurrentReservationsForTheLastUnitDoNotBothSucceed()
    {
        const int firstOrder = 9011;
        const int secondOrder = 9012;
        const int productId = 20;
        await SetStockAsync(productId, 1);

        using var firstScope = _fixture.Services.CreateScope();
        using var secondScope = _fixture.Services.CreateScope();

        var firstContext = firstScope.ServiceProvider.GetRequiredService<CatalogContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<CatalogContext>();

        // Both read available-to-promise as 1 before either has saved.
        var firstOutcome = await firstScope.ServiceProvider.GetRequiredService<IInventoryReservationService>()
            .ReserveAsync(firstOrder, [new ReservationRequest(productId, 1)], TestContext.Current.CancellationToken);
        var secondOutcome = await secondScope.ServiceProvider.GetRequiredService<IInventoryReservationService>()
            .ReserveAsync(secondOrder, [new ReservationRequest(productId, 1)], TestContext.Current.CancellationToken);

        Assert.True(firstOutcome.Success);
        Assert.True(secondOutcome.Success);

        await firstContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => secondContext.SaveChangesAsync(TestContext.Current.CancellationToken));

        // Exactly one unit was promised, to exactly one order.
        Assert.Equal(1, (await GetStockAsync(productId)).ReservedStock);
        Assert.Single(await GetReservationsAsync(firstOrder));
        Assert.Empty(await GetReservationsAsync(secondOrder));
    }
}
