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

        /// <summary>Date/time of the bill in ISO 8601 format.</summary>
        public string BillDateTime { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

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

        /// <summary>Formatted invoice number (e.g., 00001).</summary>
        public string InvoiceNumber => BillId.ToString("D5");

        /// <summary>Parsed DateTime for XAML formatting.</summary>
        public DateTime SaleDate => DateTime.TryParse(BillDateTime, out var dt) ? dt : DateTime.MinValue;

        /// <summary>Navigation — line items on this bill (not stored in DB).</summary>
        public List<BillDescription> Items { get; set; } = new();

        /// <summary>Navigation — user/cashier who processed this bill.</summary>
        public User? User { get; set; }
    }
}
