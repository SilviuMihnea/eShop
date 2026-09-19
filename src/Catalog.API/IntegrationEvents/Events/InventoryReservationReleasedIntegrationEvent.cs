using System.Text.Json.Serialization;

namespace eShop.Catalog.API.IntegrationEvents.Events;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReservationReleaseReason
{
    /// <summary>The order was cancelled, either by the customer or by a failed payment.</summary>
    Cancelled = 1,

    /// <summary>The order did not complete within the reservation window.</summary>
    Expired = 2
}

/// <summary>
/// Announces that stock held for an order has been given back. This is the compensating half of
/// the reservation flow: without it a failed release is invisible and inventory leaks silently.
/// </summary>
public record InventoryReservationReleasedIntegrationEvent(
    int OrderId,
    List<OrderStockItem> ReleasedStockItems,
    DateTime ReleasedAt,
    ReservationReleaseReason Reason) : IntegrationEvent;
