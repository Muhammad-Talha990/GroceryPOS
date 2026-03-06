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
        //  RECORD a payment (atomic: log + update bill)
        // ────────────────────────────────────────────

        /// <summary>
        /// Records a payment installment and updates the parent bill's credit columns.
        /// </summary>
        /// <param name="payment">Payment details (BillId, AmountPaid, Note).</param>
        /// <returns>Updated Bill after the payment.</returns>
        public Bill RecordPayment(CreditPayment payment)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var txn  = conn.BeginTransaction();

            try
            {
                // 1. Read current bill state
                double grandTotal, currentPaid;
                using (var readCmd = conn.CreateCommand())
                {
                    readCmd.Transaction  = txn;
                    readCmd.CommandText  = "SELECT GrandTotal, PaidAmount FROM Bill WHERE bill_id = @id;";
                    readCmd.Parameters.AddWithValue("@id", payment.BillId);
                    using var reader = readCmd.ExecuteReader();
                    if (!reader.Read())
                        throw new InvalidOperationException($"Bill #{payment.BillId} not found.");
                    grandTotal   = reader.GetDouble(0);
                    currentPaid  = reader.GetDouble(1);
                }

                double newPaid      = Math.Round(currentPaid + payment.AmountPaid, 2);
                double newRemaining = Math.Round(Math.Max(0, grandTotal - newPaid), 2);
                string newStatus    = newRemaining <= 0 ? "Paid"
                                    : newPaid > 0       ? "Partial"
                                                        : "Unpaid";

                // 2. Insert payment log
                payment.PaidAt = DateTime.Now;
                using (var logCmd = conn.CreateCommand())
                {
                    logCmd.Transaction  = txn;
                    logCmd.CommandText  = @"
                        INSERT INTO CreditPayments (BillId, AmountPaid, PaidAt, Note)
                        VALUES (@bid, @amt, @at, @note);
                        SELECT last_insert_rowid();";
                    logCmd.Parameters.AddWithValue("@bid",  payment.BillId);
                    logCmd.Parameters.AddWithValue("@amt",  payment.AmountPaid);
                    logCmd.Parameters.AddWithValue("@at",   payment.PaidAt.ToString("yyyy-MM-dd HH:mm:ss"));
                    logCmd.Parameters.AddWithValue("@note", payment.Note ?? (object)DBNull.Value);
                    payment.PaymentId = Convert.ToInt32(logCmd.ExecuteScalar());
                }

                // 3. Update bill
                using (var updCmd = conn.CreateCommand())
                {
                    updCmd.Transaction  = txn;
                    updCmd.CommandText  = @"
                        UPDATE Bill
                        SET PaidAmount     = @paid,
                            RemainingAmount = @remaining,
                            PaymentStatus  = @status
                        WHERE bill_id = @id;";
                    updCmd.Parameters.AddWithValue("@paid",      newPaid);
                    updCmd.Parameters.AddWithValue("@remaining", newRemaining);
                    updCmd.Parameters.AddWithValue("@status",    newStatus);
                    updCmd.Parameters.AddWithValue("@id",        payment.BillId);
                    updCmd.ExecuteNonQuery();
                }

                txn.Commit();
                AppLogger.Info($"CreditPayment: Bill #{payment.BillId} — Paid Rs.{payment.AmountPaid:N2}. New status: {newStatus}, Remaining: Rs.{newRemaining:N2}");

                // 4. Return updated bill stub for UI refresh
                return new Bill
                {
                    BillId          = payment.BillId,
                    GrandTotal      = grandTotal,
                    PaidAmount      = newPaid,
                    RemainingAmount = newRemaining,
                    PaymentStatus   = newStatus
                };
            }
            catch (Exception ex)
            {
                txn.Rollback();
                AppLogger.Error("CreditPaymentRepository.RecordPayment failed — rolled back", ex);
                throw;
            }
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
                SELECT * FROM CreditPayments
                WHERE BillId = @bid
                ORDER BY PaidAt DESC;";
            cmd.Parameters.AddWithValue("@bid", billId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new CreditPayment
                {
                    PaymentId  = reader.GetInt32(reader.GetOrdinal("PaymentId")),
                    BillId     = reader.GetInt32(reader.GetOrdinal("BillId")),
                    AmountPaid = reader.GetDouble(reader.GetOrdinal("AmountPaid")),
                    PaidAt     = DateTime.TryParse(reader.GetString(reader.GetOrdinal("PaidAt")), out var d) ? d : DateTime.Now,
                    Note       = reader.IsDBNull(reader.GetOrdinal("Note")) ? null : reader.GetString(reader.GetOrdinal("Note"))
                });
            }
            return list;
        }

        /// <summary>Returns total amount paid across all installments for a bill.</summary>
        public double GetTotalPaidForBill(int billId)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(AmountPaid), 0) FROM CreditPayments WHERE BillId = @bid;";
            cmd.Parameters.AddWithValue("@bid", billId);
            return Convert.ToDouble(cmd.ExecuteScalar());
        }
    }
}
