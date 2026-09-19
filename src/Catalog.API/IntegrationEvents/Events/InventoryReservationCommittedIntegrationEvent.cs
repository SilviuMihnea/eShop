namespace eShop.Catalog.API.IntegrationEvents.Events;

/// <summary>
/// Announces that an order's held stock has become sold stock. Committing happens inside the
/// catalog, so without this the transition from reserved to committed inventory would only be
/// visible as a row change.
/// </summary>
public record InventoryReservationCommittedIntegrationEvent(
    int OrderId,
    List<OrderStockItem> CommittedStockItems,
    DateTime CommittedAt) : IntegrationEvent;
