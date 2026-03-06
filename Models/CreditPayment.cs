using System;

namespace GroceryPOS.Models
{
    /// <summary>
    /// Represents one payment installment against a credit (udhar) bill.
    /// Maps to the "CreditPayments" table.
    /// </summary>
    public class CreditPayment
    {
        public int PaymentId { get; set; }
        public int BillId { get; set; }

        /// <summary>Amount paid in this installment.</summary>
        public double AmountPaid { get; set; }

        /// <summary>When this payment was recorded.</summary>
        public DateTime PaidAt { get; set; } = DateTime.Now;

        /// <summary>Optional cashier note for this payment.</summary>
        public string? Note { get; set; }
    }
}
