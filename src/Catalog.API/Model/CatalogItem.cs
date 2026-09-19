using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Pgvector;

namespace eShop.Catalog.API.Model;

public class CatalogItem
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; }

    public string? Model { get; set; }

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public string? PictureFileName { get; set; }

    public int CatalogTypeId { get; set; }

    public CatalogType? CatalogType { get; set; }

    public int CatalogBrandId { get; set; }

    public CatalogBrand? CatalogBrand { get; set; }

    // Quantity in stock
    public int AvailableStock { get; set; }

    /// <summary>
    /// Units promised to orders that have not been paid for yet. These are still part of
    /// <see cref="AvailableStock"/> — physical stock is only decremented when a reservation is
    /// committed — so this is what stops the same unit being promised to two customers.
    /// Not settable through the API: reservations are only created and settled by the
    /// inventory reservation flow.
    /// </summary>
    [JsonIgnore]
    public int ReservedStock { get; set; }

    /// <summary>
    /// Units that can still be promised to a new order.
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public int AvailableToPromise => AvailableStock - ReservedStock;

    // Available stock at which we should reorder
    public int RestockThreshold { get; set; }


    // Maximum number of units that can be in-stock at any time (due to physicial/logistical constraints in warehouses)
    public int MaxStockThreshold { get; set; }

    /// <summary>Optional embedding for the catalog item's description.</summary>
    [JsonIgnore]
    public Vector? Embedding { get; set; }

    /// <summary>
    /// True if item is on reorder
    /// </summary>
    public bool OnReorder { get; set; }

    public CatalogItem(string name) { Name = name; }


    /// <summary>
    /// Decrements the quantity of a particular item in inventory and ensures the restockThreshold hasn't
    /// been breached. If so, a RestockRequest is generated in CheckThreshold. 
    /// 
    /// If there is sufficient stock of an item, then the integer returned at the end of this call should be the same as quantityDesired. 
    /// In the event that there is not sufficient stock available, the method will remove whatever stock is available and return that quantity to the client.
    /// In this case, it is the responsibility of the client to determine if the amount that is returned is the same as quantityDesired.
    /// It is invalid to pass in a negative number. 
    /// </summary>
    /// <param name="quantityDesired"></param>
    /// <returns>int: Returns the number actually removed from stock. </returns>
    /// 
    public int RemoveStock(int quantityDesired)
    {
        if (AvailableStock == 0)
        {
            throw new CatalogDomainException($"Empty stock, product item {Name} is sold out");
        }

        if (quantityDesired <= 0)
        {
            throw new CatalogDomainException($"Item units desired should be greater than zero");
        }

        int removed = Math.Min(quantityDesired, this.AvailableStock);

        this.AvailableStock -= removed;

        return removed;
    }

    /// <summary>
    /// Increments the quantity of a particular item in inventory.
    /// <param name="quantity"></param>
    /// <returns>int: Returns the quantity that has been added to stock</returns>
    /// </summary>
    public int AddStock(int quantity)
    {
        int original = this.AvailableStock;

        // The quantity that the client is trying to add to stock is greater than what can be physically accommodated in the Warehouse
        if ((this.AvailableStock + quantity) > this.MaxStockThreshold)
        {
            // For now, this method only adds new units up maximum stock threshold. In an expanded version of this application, we
            //could include tracking for the remaining units and store information about overstock elsewhere. 
            this.AvailableStock += (this.MaxStockThreshold - this.AvailableStock);
        }
        else
        {
            this.AvailableStock += quantity;
        }

        this.OnReorder = false;

        return this.AvailableStock - original;
    }

    /// <summary>
    /// Promises units to an order without taking them out of physical stock. Reserving is what
    /// makes the stock check binding: between this call and the order being paid for, the units
    /// are no longer available to anyone else.
    /// </summary>
    /// <param name="units">The number of units to hold. Must be greater than zero.</param>
    public void Reserve(int units)
    {
        if (units <= 0)
        {
            throw new CatalogDomainException($"Units to reserve should be greater than zero, but was {units}");
        }

        if (units > AvailableToPromise)
        {
            throw new CatalogDomainException(
                $"Insufficient stock for product item {Name}: {units} unit(s) requested, {AvailableToPromise} available to promise");
        }

        ReservedStock += units;
    }

    /// <summary>
    /// Turns a hold into a sale: the units leave physical stock and stop being reserved.
    /// </summary>
    /// <param name="units">The number of previously reserved units to commit.</param>
    public void CommitReservation(int units)
    {
        if (units <= 0)
        {
            throw new CatalogDomainException($"Units to commit should be greater than zero, but was {units}");
        }

        if (units > ReservedStock)
        {
            throw new CatalogDomainException(
                $"Cannot commit {units} unit(s) of product item {Name}: only {ReservedStock} unit(s) are reserved");
        }

        ReservedStock -= units;
        AvailableStock -= units;
    }

    /// <summary>
    /// Gives a hold back so the units can be promised to someone else. Physical stock is
    /// untouched, because a reservation never took any.
    /// </summary>
    /// <param name="units">The number of previously reserved units to release.</param>
    public void ReleaseReservation(int units)
    {
        if (units <= 0)
        {
            throw new CatalogDomainException($"Units to release should be greater than zero, but was {units}");
        }

        if (units > ReservedStock)
        {
            throw new CatalogDomainException(
                $"Cannot release {units} unit(s) of product item {Name}: only {ReservedStock} unit(s) are reserved");
        }

        ReservedStock -= units;
    }
}
