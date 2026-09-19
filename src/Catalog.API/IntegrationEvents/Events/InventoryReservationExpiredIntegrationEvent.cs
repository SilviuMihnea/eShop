namespace eShop.Catalog.API.IntegrationEvents.Events;

/// <summary>
/// Tells the ordering service that an order's stock reservation lapsed before the order
/// completed, so the order must not go any further.
///
/// This is deliberately separate from <see cref="InventoryReservationReleasedIntegrationEvent"/>,
/// which records the inventory movement. Ordering must not subscribe to that one: a release also
/// happens as a result of a cancellation that ordering itself initiated, and reacting to it would
/// have ordering cancel orders in response to its own cancellations.
/// </summary>
public record InventoryReservationExpiredIntegrationEvent(int OrderId, DateTime ExpiredAt) : IntegrationEvent;
