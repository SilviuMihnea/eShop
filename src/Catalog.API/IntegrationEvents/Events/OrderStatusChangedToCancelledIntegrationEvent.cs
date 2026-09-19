namespace eShop.Catalog.API.IntegrationEvents.Events;

// Only the order id is declared here: the catalog's copy of a contract carries just the fields
// it consumes, the same way its copy of OrderStatusChangedToPaidIntegrationEvent does. The
// publisher's extra fields are ignored when the message is deserialised.
public record OrderStatusChangedToCancelledIntegrationEvent(int OrderId) : IntegrationEvent;
