using System;

namespace GroceryPOS.Models
{
    /// <summary>
    /// Represents a single entry in the Customer Ledger (Sale, Payment, or Return).
    /// Used for chronological audit trails and running balance calculations.
    /// </summary>
    public class CustomerLedgerEntry
    {
        public int LedgerId { get; set; }
        
        public int CustomerId { get; set; }
        public Customer? Customer { get; set; }

        /// <summary>Chronological order of the entry.</summary>
        public DateTime EntryDate { get; set; } = DateTime.Now;

        /// <summary>Type of entry: SALE, PAYMENT, RETURN, ADJUSTMENT.</summary>
        public string Type { get; set; } = "SALE";

        /// <summary>Invoice #, Receipt #, or Payment ID for reference.</summary>
        public string? ReferenceId { get; set; }

        /// <summary>Friendly description of the transaction.</summary>
        public string? Description { get; set; }

        /// <summary>Amount increasing the customer's liability (e.g., Sale total).</summary>
        public double Debit { get; set; }

        /// <summary>Amount decreasing the customer's liability (e.g., Payment or Return).</summary>
        public double Credit { get; set; }

        /// <summary>Total outstanding balance AFTER this entry.</summary>
        public double RunningBalance { get; set; }

        // UI Helpers
        public bool IsSale => Type == "SALE";
        public bool IsPayment => Type == "PAYMENT";
        public bool IsReturn => Type == "RETURN";
        
        public string ColorBrush => Type switch
        {
            "SALE"    => "#60A5FA", // Blue-400
            "PAYMENT" => "#22C55E", // Green-500
            "RETURN"  => "#F59E0B", // Amber-500
            _         => "#94A3B8"  // Slate-400
        };
    }
}
