namespace eShop.Ordering.API.Application.IntegrationEvents.Events;

/// <summary>
/// Catalog copy lives in Catalog.API. Matched across services by type name, which is what the
/// event bus uses as the RabbitMQ routing key.
/// </summary>
public record InventoryReservationExpiredIntegrationEvent(int OrderId, DateTime ExpiredAt) : IntegrationEvent;
