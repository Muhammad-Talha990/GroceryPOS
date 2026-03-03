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
        public string Name { get; set; } = string.Empty;
        public string PrimaryPhone { get; set; } = string.Empty;
        public string? Address { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>Navigation — other phones (not stored in Customers table).</summary>
        public List<CustomerPhone> OtherPhones { get; set; } = new();

        // ── Calculated Properties (Optional Enhancements) ──
        public int BillCount { get; set; }
        public double TotalAmount { get; set; }
        public DateTime? LastVisitDate { get; set; }
    }
}
