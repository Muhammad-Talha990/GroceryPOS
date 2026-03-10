using System;
using System.Collections.Generic;

namespace GroceryPOS.Models
{
    /// <summary>
    /// Represents a completed sale/bill transaction.
    /// Maps to the "Bills" table in the normalized 3NF schema.
    /// Totals and remaining amounts are calculated from joined tables.
    /// </summary>
    public class Bill
    {
        /// <summary>Database Internal ID (Primary Key).</summary>
        public int BillId { get; set; }

        /// <summary>Formatted invoice number (e.g., 00001).</summary>
        public string InvoiceNumber => BillId.ToString("D5");

        /// <summary>Date/time when the bill was created.</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>Compatibility shim for old code.</summary>
        public DateTime BillDateTime { get => CreatedAt; set => CreatedAt = value; }
        public DateTime SaleDate => CreatedAt;

        /// <summary>Foreign Key to Customers table.</summary>
        public int? CustomerId { get; set; }
        public Customer? Customer { get; set; }

        /// <summary>Foreign Key to Users table.</summary>
        public int? UserId { get; set; }
        public User? User { get; set; }

        /// <summary>Status of the bill ('Completed', 'Cancelled').</summary>
        public string Status { get; set; } = "Completed";

        /// <summary>Type of the transaction ('Sale', 'Return').</summary>
        public string Type { get; set; } = "Sale";

        /// <summary>Links a return to its original sale bill.</summary>
        public int? ParentBillId { get; set; }

        /// <summary>Compatibility shim for old code.</summary>
        public int? ReferenceBillId => ParentBillId;

        /// <summary>Helper property for UI.</summary>
        public bool IsReturn => Type == "Return";

        /// <summary>Grandparent reference for complex return logic (optional).</summary>
        public Bill? ParentBill { get; set; }

        /// <summary>Related return history for the original bill (optional).</summary>
        public List<Bill> ReturnHistory { get; set; } = new();

        /// <summary>Flat tax amount applied to this bill.</summary>
        public double TaxAmount { get; set; }

        /// <summary>Flat discount amount applied to this bill.</summary>
        public double DiscountAmount { get; set; }

        // ── Calculated Properties (Populated via Repositories) ──

        /// <summary>Sum of (Quantity * UnitPrice) for all line items.</summary>
        public double SubTotal { get; set; }

        /// <summary>Final total: SubTotal + TaxAmount - DiscountAmount.</summary>
        public double GrandTotal => SubTotal + TaxAmount - DiscountAmount;

        /// <summary>Total amount successfully paid (from Payments table).</summary>
        public double PaidAmount { get; set; }

        /// <summary>Outstanding balance: GrandTotal - PaidAmount.</summary>
        public double RemainingAmount => Math.Max(0, GrandTotal - PaidAmount);

        /// <summary>Navigation property for line items.</summary>
        public List<BillDescription> Items { get; set; } = new();

        /// <summary>Returns calculated based on this bill.</summary>
        public List<BillDescription> ReturnedItems { get; set; } = new();

        // ── UI Helpers ──

        public bool IsPrinted { get; set; }
        public DateTime? PrintedAt { get; set; }
        public int PrintAttempts { get; set; }
        public bool IsPendingPrint => !IsPrinted && Status != "Cancelled";

        public string PaymentStatus => RemainingAmount <= 0 ? "Paid" : (PaidAmount > 0 ? "Partial" : "Unpaid");

        public string PaymentStatusColor => PaymentStatus switch
        {
            "Paid"    => "#22C55E",
            "Partial" => "#F59E0B",
            "Unpaid"  => "#EF4444",
            _         => "#94A3B8"
        };

        public bool HasPendingCredit => RemainingAmount > 0 && Status != "Cancelled";

        /// <summary>Calculated summary for UI display of returns.</summary>
        public double RemainingDueAfterThisReturn { get; set; }

        // ── Storage/Transient Properties (For Receipt & Legacy Support) ──
        
        /// <summary>Amount of cash physically tendered by the customer.</summary>
        public double CashReceived { get; set; }

        /// <summary>Change returned to the customer.</summary>
        public double ChangeGiven { get; set; }

        /// <summary>Optional billing address for the customer.</summary>
        public string? BillingAddress { get; set; }

        /// <summary>Payment method used for this bill (Cash, Card, Credit).</summary>
        public string PaymentMethod { get; set; } = "Cash";
    }
}
