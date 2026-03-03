using System;
using System.Collections.Generic;

namespace GroceryPOS.Models
{
    /// <summary>
    /// Represents a completed sale/bill transaction.
    /// Maps to the "Bill" table in SQLite.
    /// </summary>
    public class Bill
    {
        /// <summary>Auto-increment primary key.</summary>
        public int BillId { get; set; }

        /// <summary>Date/time of the bill.</summary>
        public DateTime BillDateTime { get; set; } = DateTime.Now;

        /// <summary>Sum of all line item totals before discount/tax.</summary>
        public double SubTotal { get; set; }

        /// <summary>Flat discount amount applied to the bill.</summary>
        public double DiscountAmount { get; set; }

        /// <summary>Tax amount applied to the bill.</summary>
        public double TaxAmount { get; set; }

        /// <summary>Final total: SubTotal - DiscountAmount + TaxAmount.</summary>
        public double GrandTotal { get; set; }

        /// <summary>Cash received from the customer.</summary>
        public double CashReceived { get; set; }

        /// <summary>Change returned: CashReceived - GrandTotal.</summary>
        public double ChangeGiven { get; set; }

        /// <summary>ID of the user/cashier who processed the bill.</summary>
        public int? UserId { get; set; }

        /// <summary>ID of the customer (nullable for walk-ins).</summary>
        public int? CustomerId { get; set; }

        /// <summary>Formatted invoice number (e.g., 00001).</summary>
        public string InvoiceNumber => BillId.ToString("D5");

        /// <summary>Parsed DateTime for XAML formatting.</summary>
        public DateTime SaleDate => BillDateTime;

        /// <summary>Navigation — line items on this bill (not stored in DB).</summary>
        public List<BillDescription> Items { get; set; } = new();

        /// <summary>Navigation — user/cashier who processed this bill.</summary>
        public User? User { get; set; }

        /// <summary>Navigation — customer associated with this bill.</summary>
        public Customer? Customer { get; set; }

        /// <summary>Current status of the bill (e.g., Completed, Cancelled, Replaced).</summary>
        public string Status { get; set; } = "Completed";

        /// <summary>Reference to the original bill ID for corrections (legacy).</summary>
        public int? ReferenceBillId { get; set; }

        /// <summary>Professional Type: "Sale" or "Return".</summary>
        public string Type { get; set; } = "Sale";

        /// <summary>FK to parent Bill for returns.</summary>
        public int? ParentBillId { get; set; }

        // --- Print Tracking ---
        /// <summary>Whether the receipt has been successfully printed.</summary>
        public bool IsPrinted { get; set; }
        /// <summary>When the receipt was successfully printed.</summary>
        public DateTime? PrintedAt { get; set; }
        /// <summary>Number of failed or successful print attempts.</summary>
        public int PrintAttempts { get; set; }

        /// <summary>Helper to check if this bill needs printing.</summary>
        public bool IsPendingPrint => !IsPrinted && Status != "Cancelled";

        /// <summary>Helper to check if this is a return bill.</summary>
        public bool IsReturn => Type == "Return";
    }
}
