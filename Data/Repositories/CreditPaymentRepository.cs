using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using GroceryPOS.Helpers;
using GroceryPOS.Models;

namespace GroceryPOS.Data.Repositories
{
    /// <summary>
    /// Data access for CreditPayments — individual payment installments against credit bills.
    /// Also updates the parent Bill's PaidAmount / RemainingAmount / PaymentStatus atomically.
    /// </summary>
    public class CreditPaymentRepository
    {
        // ────────────────────────────────────────────
        //  RECORD a payment (atomic log)
        // ────────────────────────────────────────────

        /// <summary>
        /// Records a payment installment in the unified Payments table.
        /// </summary>
        public void RecordPayment(CreditPayment payment)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            
            cmd.CommandText = @"
                INSERT INTO Payments (BillId, Amount, PaymentMethod, TransactionType, PaidAt)
                VALUES (@bid, @amt, 'Cash', 'Credit Payment', @at);
                SELECT last_insert_rowid();";
            
            cmd.Parameters.AddWithValue("@bid", payment.BillId);
            cmd.Parameters.AddWithValue("@amt", Math.Round(payment.AmountPaid, 2));
            cmd.Parameters.AddWithValue("@at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            
            payment.PaymentId = Convert.ToInt32(cmd.ExecuteScalar());
            AppLogger.Info($"CreditPayment recorded: BillId={payment.BillId}, Amount={payment.AmountPaid:N2}");
        }

        // ────────────────────────────────────────────
        //  READ operations
        // ────────────────────────────────────────────

        /// <summary>Returns all payment installments for a specific bill, newest first.</summary>
        public List<CreditPayment> GetPaymentsForBill(int billId)
        {
            var list = new List<CreditPayment>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT PaymentId, BillId, Amount, PaidAt
                FROM Payments
                WHERE BillId = @bid
                ORDER BY PaidAt DESC;";
            cmd.Parameters.AddWithValue("@bid", billId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new CreditPayment
                {
                    PaymentId  = reader.GetInt32(0),
                    BillId     = reader.GetInt32(1),
                    AmountPaid = reader.GetDouble(2),
                    PaidAt     = reader.GetDateTime(3)
                });
            }
            return list;
        }

        /// <summary>Returns total amount paid across all installments for a bill.</summary>
        public double GetTotalPaidForBill(int billId)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(Amount), 0) FROM Payments WHERE BillId = @bid;";
            cmd.Parameters.AddWithValue("@bid", billId);
            return Convert.ToDouble(cmd.ExecuteScalar());
        }
    }
}
