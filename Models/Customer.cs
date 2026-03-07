using System;
using System.Collections.Generic;

namespace GroceryPOS.Models
{
    /// <summary>
    /// Represents a customer in the system.
    /// Maps to the "Customers" table.
    /// </summary>
    public class Customer
    {
        public int CustomerId { get; set; }

        /// <summary>Full name of the customer (stored in FullName column).</summary>
        public string FullName { get; set; } = string.Empty;

        /// <summary>Backward-compat alias — maps to FullName.</summary>
        public string Name
        {
            get => FullName;
            set => FullName = value;
        }

        public string PrimaryPhone { get; set; } = string.Empty;
        public string? SecondaryPhone { get; set; }
        public string? Address { get; set; }
        public string? Address2 { get; set; }
        public string? Address3 { get; set; }

        /// <summary>False = soft-deleted. Only active customers appear in billing search.</summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>Navigation — other phones (not stored in Customers table).</summary>
        public List<CustomerPhone> OtherPhones { get; set; } = new();

        // ── Calculated Properties ──
        public int BillCount { get; set; }
        public double TotalAmount { get; set; }
        public DateTime? LastVisitDate { get; set; }

        /// <summary>
        /// Total outstanding credit (sum of RemainingAmount on unpaid bills).
        /// Populated by queries — not stored in DB.
        /// </summary>
        public double PendingCredit { get; set; }

        /// <summary>Display helper for status badge.</summary>
        public string StatusLabel => IsActive ? "Active" : "Inactive";

        /// <summary>Display helper — pending credit formatted.</summary>
        public string PendingCreditDisplay =>
            PendingCredit > 0 ? $"Rs. {PendingCredit:N0}" : "—";
    }
}
