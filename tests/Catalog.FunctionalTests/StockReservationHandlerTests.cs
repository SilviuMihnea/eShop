using System.Text.Json;
using eShop.Catalog.API.Infrastructure;
using eShop.Catalog.API.IntegrationEvents.Events;
using eShop.Catalog.API.Model;
using eShop.EventBus.Abstractions;
using eShop.EventBus.Events;
using eShop.IntegrationEventLogEF;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace eShop.Catalog.FunctionalTests;

/// <summary>
/// Covers the stock steps of the order saga: validation now takes a real hold on inventory
/// instead of doing a read-only availability check, and payment turns that hold into a sale.
/// The confirm/reject decision and the committed quantities both have to match what was held.
///
/// The handler is resolved the same way the event bus resolves it, so these tests also prove the
/// subscription is wired. There is no RabbitMQ in this fixture, so the publish attempt inside the
/// handler fails and is swallowed by CatalogIntegrationEventService by design — the outbox row is
/// still written, which is what we assert on. That is also why the entry's State is not asserted.
/// </summary>
public sealed class StockReservationHandlerTests : IClassFixture<CatalogApiFixture>
{
    private readonly CatalogApiFixture _fixture;

    public StockReservationHandlerTests(CatalogApiFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task HandleAsync(int orderId, params (int ProductId, int Units)[] lines)
    {
        using var scope = _fixture.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredKeyedService<IIntegrationEventHandler>(
            typeof(OrderStatusChangedToAwaitingValidationIntegrationEvent));

        var @event = new OrderStatusChangedToAwaitingValidationIntegrationEvent(
            orderId,
            lines.Select(line => new OrderStockItem(line.ProductId, line.Units)).ToList());

        await handler.Handle(@event);
    }

    /// <summary>Drives the payment side of the saga, which turns the hold into a sale.</summary>
    private async Task HandlePaidAsync(int orderId, params (int ProductId, int Units)[] lines)
    {
        using var scope = _fixture.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredKeyedService<IIntegrationEventHandler>(
            typeof(OrderStatusChangedToPaidIntegrationEvent));

        var @event = new OrderStatusChangedToPaidIntegrationEvent(
            orderId,
            lines.Select(line => new OrderStockItem(line.ProductId, line.Units)).ToList());

        await handler.Handle(@event);
    }

    private async Task<ReservationStatus?> GetReservationStatusAsync(int orderId, int productId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var reservation = await context.InventoryReservations
            .SingleOrDefaultAsync(r => r.OrderId == orderId && r.ProductId == productId,
                TestContext.Current.CancellationToken);
        return reservation?.Status;
    }

    private async Task SetStockAsync(int productId, int availableStock)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var item = await context.CatalogItems.SingleAsync(i => i.Id == productId, TestContext.Current.CancellationToken);
        item.AvailableStock = availableStock;
        item.ReservedStock = 0;
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<(int AvailableStock, int ReservedStock)> GetStockAsync(int productId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var item = await context.CatalogItems.SingleAsync(i => i.Id == productId, TestContext.Current.CancellationToken);
        return (item.AvailableStock, item.ReservedStock);
    }

    private async Task<int> CountReservationsAsync(int orderId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        return await context.InventoryReservations
            .CountAsync(r => r.OrderId == orderId, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The short names of the integration events the handler enqueued for an order, read out of
    /// the outbox. The order id is pulled from the serialised payload so the match is exact.
    /// </summary>
    private async Task<List<string>> GetOutboxEventNamesAsync(int orderId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var entries = await context.Set<IntegrationEventLogEntry>()
            .ToListAsync(TestContext.Current.CancellationToken);

        return [.. entries
            .Where(entry => GetOrderId(entry.Content) == orderId)
            .Select(entry => entry.EventTypeShortName)];
    }

    /// <summary>
    /// The outbox entries for an order, deserialised back into a specific event type.
    /// </summary>
    private async Task<List<TEvent>> GetOutboxEventsAsync<TEvent>(int orderId)
        where TEvent : IntegrationEvent
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var entries = await context.Set<IntegrationEventLogEntry>()
            .ToListAsync(TestContext.Current.CancellationToken);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var events = new List<TEvent>();

        foreach (var entry in entries.Where(e => e.EventTypeShortName == typeof(TEvent).Name))
        {
            var @event = JsonSerializer.Deserialize<TEvent>(entry.Content, options);
            if (@event is not null && GetOrderId(entry.Content) == orderId)
            {
                events.Add(@event);
            }
        }

        return events;
    }

    private static int? GetOrderId(string content)
    {
        using var document = JsonDocument.Parse(content);
        return document.RootElement.TryGetProperty("OrderId", out var value) && value.TryGetInt32(out var orderId)
            ? orderId
            : null;
    }

    [Fact]
    public async Task AvailableStockIsHeldAndTheOrderIsConfirmed()
    {
        const int orderId = 9101;
        const int productId = 31;
        await SetStockAsync(productId, 12);

        await HandleAsync(orderId, (productId, 4));

        var stock = await GetStockAsync(productId);
        Assert.Equal(12, stock.AvailableStock);
        Assert.Equal(4, stock.ReservedStock);
        Assert.Equal(1, await CountReservationsAsync(orderId));

        Assert.Equal(
            nameof(OrderStockConfirmedIntegrationEvent),
            Assert.Single(await GetOutboxEventNamesAsync(orderId)));
    }

    [Fact]
    public async Task ShortStockIsRejectedAndNothingIsHeld()
    {
        const int orderId = 9102;
        const int productId = 32;
        await SetStockAsync(productId, 2);

        await HandleAsync(orderId, (productId, 5));

        Assert.Equal(0, (await GetStockAsync(productId)).ReservedStock);
        Assert.Equal(0, await CountReservationsAsync(orderId));

        Assert.Equal(
            nameof(OrderStockRejectedIntegrationEvent),
            Assert.Single(await GetOutboxEventNamesAsync(orderId)));
    }

    [Fact]
    public async Task OneShortLineRejectsTheWholeOrderWithoutHoldingTheOthers()
    {
        const int orderId = 9103;
        const int plentiful = 33;
        const int scarce = 34;
        await SetStockAsync(plentiful, 20);
        await SetStockAsync(scarce, 1);

        await HandleAsync(orderId, (plentiful, 2), (scarce, 9));

        // The satisfiable line must not be held on its own, otherwise a rejected order would
        // leave stock stranded with nothing to release it.
        Assert.Equal(0, (await GetStockAsync(plentiful)).ReservedStock);
        Assert.Equal(0, (await GetStockAsync(scarce)).ReservedStock);
        Assert.Equal(0, await CountReservationsAsync(orderId));

        Assert.Equal(
            nameof(OrderStockRejectedIntegrationEvent),
            Assert.Single(await GetOutboxEventNamesAsync(orderId)));
    }

    [Fact]
    public async Task RejectionReportsWhichLinesWereShort()
    {
        const int orderId = 9104;
        const int plentiful = 35;
        const int scarce = 36;
        await SetStockAsync(plentiful, 20);
        await SetStockAsync(scarce, 1);

        await HandleAsync(orderId, (plentiful, 2), (scarce, 9));

        var rejection = Assert.Single(
            await GetOutboxEventsAsync<OrderStockRejectedIntegrationEvent>(orderId));

        // Ordering turns the items without stock into the cancellation reason shown to the
        // customer, so the flags have to distinguish the short line from the available one.
        Assert.True(rejection.OrderStockItems.Single(i => i.ProductId == plentiful).HasStock);
        Assert.False(rejection.OrderStockItems.Single(i => i.ProductId == scarce).HasStock);
    }

    [Fact]
    public async Task UnknownProductIsRejectedRatherThanSkipped()
    {
        const int orderId = 9105;

        await HandleAsync(orderId, (int.MaxValue, 1));

        Assert.Equal(0, await CountReservationsAsync(orderId));

        // The read-only check this replaced ignored products it could not find, which let an
        // order for a deleted item pass validation.
        Assert.Equal(
            nameof(OrderStockRejectedIntegrationEvent),
            Assert.Single(await GetOutboxEventNamesAsync(orderId)));
    }

    [Fact]
    public async Task RedeliveryDoesNotHoldTheUnitsTwice()
    {
        const int orderId = 9106;
        const int productId = 37;
        await SetStockAsync(productId, 10);

        await HandleAsync(orderId, (productId, 3));
        await HandleAsync(orderId, (productId, 3));

        Assert.Equal(3, (await GetStockAsync(productId)).ReservedStock);
        Assert.Equal(1, await CountReservationsAsync(orderId));

        // Both deliveries confirm, because the hold from the first one still stands.
        var published = await GetOutboxEventNamesAsync(orderId);
        Assert.Equal(2, published.Count);
        Assert.All(published, name => Assert.Equal(nameof(OrderStockConfirmedIntegrationEvent), name));
    }

    [Fact]
    public async Task UnitsHeldByOneOrderAreNotAvailableToAnother()
    {
        const int firstOrder = 9107;
        const int secondOrder = 9108;
        const int productId = 38;
        await SetStockAsync(productId, 1);

        await HandleAsync(firstOrder, (productId, 1));
        await HandleAsync(secondOrder, (productId, 1));

        Assert.Equal(1, (await GetStockAsync(productId)).ReservedStock);
        Assert.Equal(1, await CountReservationsAsync(firstOrder));
        Assert.Equal(0, await CountReservationsAsync(secondOrder));

        Assert.Equal(
            nameof(OrderStockConfirmedIntegrationEvent),
            Assert.Single(await GetOutboxEventNamesAsync(firstOrder)));
        Assert.Equal(
            nameof(OrderStockRejectedIntegrationEvent),
            Assert.Single(await GetOutboxEventNamesAsync(secondOrder)));
    }

    [Fact]
    public async Task PaymentCommitsTheHoldAndAnnouncesIt()
    {
        const int orderId = 9201;
        const int productId = 41;
        await SetStockAsync(productId, 10);

        await HandleAsync(orderId, (productId, 3));
        await HandlePaidAsync(orderId, (productId, 3));

        var stock = await GetStockAsync(productId);
        Assert.Equal(7, stock.AvailableStock);
        Assert.Equal(0, stock.ReservedStock);
        Assert.Equal(ReservationStatus.Committed, await GetReservationStatusAsync(orderId, productId));

        var published = await GetOutboxEventNamesAsync(orderId);
        Assert.Contains(nameof(OrderStockConfirmedIntegrationEvent), published);
        Assert.Contains(nameof(InventoryReservationCommittedIntegrationEvent), published);
    }

    [Fact]
    public async Task CommittedEventCarriesWhatWasActuallyHeld()
    {
        const int orderId = 9202;
        const int productId = 42;
        await SetStockAsync(productId, 10);

        await HandleAsync(orderId, (productId, 2));
        await HandlePaidAsync(orderId, (productId, 2));

        var committed = Assert.Single(
            await GetOutboxEventsAsync<InventoryReservationCommittedIntegrationEvent>(orderId));

        var line = Assert.Single(committed.CommittedStockItems);
        Assert.Equal(productId, line.ProductId);
        Assert.Equal(2, line.Units);
    }

    [Fact]
    public async Task PaymentRedeliveryDoesNotDecrementStockTwice()
    {
        const int orderId = 9203;
        const int productId = 43;
        await SetStockAsync(productId, 10);

        await HandleAsync(orderId, (productId, 4));
        await HandlePaidAsync(orderId, (productId, 4));
        await HandlePaidAsync(orderId, (productId, 4));

        Assert.Equal(6, (await GetStockAsync(productId)).AvailableStock);

        // The second delivery finds nothing outstanding, so it announces nothing.
        Assert.Single(await GetOutboxEventsAsync<InventoryReservationCommittedIntegrationEvent>(orderId));
    }

    /// <summary>
    /// The quantities on the payment event are not trusted: what gets sold is what was held.
    /// Before reservations existed this handler decremented whatever the event asked for.
    /// </summary>
    [Fact]
    public async Task PaymentCommitsTheHeldQuantityNotTheQuantityOnTheEvent()
    {
        const int orderId = 9204;
        const int productId = 44;
        await SetStockAsync(productId, 10);

        await HandleAsync(orderId, (productId, 2));
        await HandlePaidAsync(orderId, (productId, 99));

        Assert.Equal(8, (await GetStockAsync(productId)).AvailableStock);
        Assert.Equal(0, (await GetStockAsync(productId)).ReservedStock);
    }

    [Fact]
    public async Task PaymentForAnOrderWithNoHoldLeavesStockAlone()
    {
        const int orderId = 9205;
        const int productId = 45;
        await SetStockAsync(productId, 10);

        // No validation step ran, so there is nothing to commit.
        await HandlePaidAsync(orderId, (productId, 3));

        var stock = await GetStockAsync(productId);
        Assert.Equal(10, stock.AvailableStock);
        Assert.Equal(0, stock.ReservedStock);
        Assert.Empty(await GetOutboxEventsAsync<InventoryReservationCommittedIntegrationEvent>(orderId));
    }
}
