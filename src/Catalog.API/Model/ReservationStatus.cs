using System.Text.Json.Serialization;

namespace eShop.Catalog.API.Model;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReservationStatus
{
    /// <summary>Units are held for the order but physical stock has not been decremented.</summary>
    Reserved = 1,

    /// <summary>The order was paid for and the held units were taken out of physical stock.</summary>
    Committed = 2,

    /// <summary>The hold was given back, either because the order was cancelled or because it expired.</summary>
    Released = 3
}
