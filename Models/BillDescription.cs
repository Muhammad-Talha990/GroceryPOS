namespace GroceryPOS.Models
{
    /// <summary>
    /// Represents a single line item on a bill.
    /// Stores the UnitPrice at time of sale for price history integrity.
    /// Maps to the "BillDescription" table in SQLite.
    /// </summary>
    public class BillDescription
    {
        /// <summary>Auto-increment primary key.</summary>
        public int Id { get; set; }

        /// <summary>Foreign key to Bill.bill_id.</summary>
        public int BillId { get; set; }

        /// <summary>Foreign key to Item.itemId (barcode).</summary>
        public string ItemId { get; set; } = string.Empty;

        /// <summary>Quantity of this item sold.</summary>
        public int Quantity { get; set; }

        /// <summary>Unit price at time of sale (frozen for history).</summary>
        public double UnitPrice { get; set; }

        /// <summary>Line total: Quantity × UnitPrice.</summary>
        public double TotalPrice { get; set; }

        // ── Convenience properties (not stored in DB) ──

        /// <summary>Item description, populated by JOIN or lookup.</summary>
        public string ItemDescription { get; set; } = string.Empty;
    }
}
