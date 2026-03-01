using System;

namespace GroceryPOS.Models
{
    /// <summary>
    /// Represents an entry in the stock ledger for purchase history.
    /// </summary>
    public class Stock
    {
        /// <summary>Primary Key (Auto Increment).</summary>
        public int Id { get; set; }

        /// <summary>The product barcode (Item.itemId).</summary>
        public string ProductId { get; set; } = string.Empty;

        /// <summary>The linked Supplier Bill ID (SUP-YYYY-XXXX).</summary>
        public string BillId { get; set; } = string.Empty;

        /// <summary>The quantity of the product purchased.</summary>
        public int Quantity { get; set; }

        /// <summary>Timestamp of the stock entry (auto-generated in DB).</summary>
        public DateTime SystemDate { get; set; } = DateTime.Now;

        /// <summary>Path to the physical bill image.</summary>
        public string? ImagePath { get; set; }
        
        // Populated by JOIN queries for display
        public string? ProductName { get; set; }
    }
}
