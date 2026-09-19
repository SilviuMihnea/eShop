namespace eShop.Catalog.API;

public class InventoryOptions
{
    /// <summary>
    /// How long a reservation is honoured before it is eligible to be given back. An order
    /// that has not been paid for within this window has its held units released so they
    /// are not promised to an order that never completes.
    /// </summary>
    public int ReservationTimeoutMinutes { get; set; } = 15;

    /// <summary>
    /// How often expired reservations are swept up and released.
    /// </summary>
    public int ExpiryCheckSeconds { get; set; } = 60;
}
