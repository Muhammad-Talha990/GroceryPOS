using System;

namespace GroceryPOS.Models
{
    /// <summary>
    /// Represents a return transaction for a specific product from an original bill.
    /// Maps to "BILL_RETURNS" table.
    /// </summary>
    public class BillReturn
    {
        /// <summary>Auto-increment primary key.</summary>
        public int Id { get; set; }

        /// <summary>Foreign Key to the original bill.</summary>
        public int BillId { get; set; }

        /// <summary>The product barcode.</summary>
        public string ProductId { get; set; } = string.Empty;

        /// <summary>Quantity being returned.</summary>
        public int ReturnQuantity { get; set; }

        /// <summary>The date when the original bill was generated.</summary>
        public string OriginalBillDate { get; set; } = string.Empty;

        /// <summary>The date of the return transaction.</summary>
        public string ReturnDate { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        /// <summary>Reference to the newly generated return bill ID (formatted string).</summary>
        public string ReturnBillId { get; set; } = string.Empty;

        // ── Navigation / Display Helpers ──
        
        /// <summary>Item description, helpful for UI.</summary>
        public string? ProductDescription { get; set; }
    }
}
