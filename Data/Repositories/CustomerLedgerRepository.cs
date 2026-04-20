using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using GroceryPOS.Helpers;
using GroceryPOS.Models;

namespace GroceryPOS.Data.Repositories
{
    public class CustomerLedgerRepository
    {
        /// <summary>
        /// Records an entry in the ledger.
        /// CALCULATES THE RUNNING BALANCE based on the previous entry for this customer.
        /// This must be called within the same transaction as the Sale/Payment/Return.
        /// </summary>
        public void AddEntry(CustomerLedgerEntry entry, SqliteConnection conn, SqliteTransaction txn)
        {
            // 1. Get the latest balance for this customer
            double previousBalance = GetLatestBalanceInternal(entry.CustomerId, conn, txn);
            
            // 2. Calculate new balance: Balance + Debit - Credit
            entry.RunningBalance = Math.Round(previousBalance + entry.Debit - entry.Credit, 2);

            // 3. Insert entry
            using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            cmd.CommandText = @"
                INSERT INTO CustomerLedger (CustomerId, Type, ReferenceId, Description, Debit, Credit, RunningBalance, EntryDate)
                VALUES (@cid, @type, @ref, @desc, @debit, @credit, @bal, @date);
                SELECT last_insert_rowid();
            ";
            cmd.Parameters.AddWithValue("@cid", entry.CustomerId);
            cmd.Parameters.AddWithValue("@type", entry.Type);
            cmd.Parameters.AddWithValue("@ref", (object?)entry.ReferenceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@desc", (object?)entry.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@debit", entry.Debit);
            cmd.Parameters.AddWithValue("@credit", entry.Credit);
            cmd.Parameters.AddWithValue("@bal", entry.RunningBalance);
            cmd.Parameters.AddWithValue("@date", entry.EntryDate.ToString("yyyy-MM-dd HH:mm:ss"));

            entry.LedgerId = Convert.ToInt32(cmd.ExecuteScalar());
        }

        private double GetLatestBalanceInternal(int customerId, SqliteConnection conn, SqliteTransaction? txn)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            // Get the balance from the latest entry by LedgerId (safest for chronological consistency)
            cmd.CommandText = "SELECT RunningBalance FROM CustomerLedger WHERE CustomerId = @cid ORDER BY LedgerId DESC LIMIT 1;";
            cmd.Parameters.AddWithValue("@cid", customerId);
            
            var result = cmd.ExecuteScalar();
            return result != null ? Convert.ToDouble(result) : 0;
        }

        public List<CustomerLedgerEntry> GetByCustomer(int customerId)
        {
            var list = new List<CustomerLedgerEntry>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT LedgerId, CustomerId, Type, ReferenceId, Description, Debit, Credit, RunningBalance, EntryDate
                FROM CustomerLedger
                WHERE CustomerId = @cid
                ORDER BY LedgerId ASC;";
            cmd.Parameters.AddWithValue("@cid", customerId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new CustomerLedgerEntry
                {
                    LedgerId       = reader.GetInt32(0),
                    CustomerId     = reader.GetInt32(1),
                    Type           = reader.GetString(2),
                    ReferenceId    = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Description    = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Debit          = reader.GetDouble(5),
                    Credit         = reader.GetDouble(6),
                    RunningBalance = reader.GetDouble(7),
                    EntryDate      = reader.GetDateTime(8)
                });
            }
            return list;
        }

        /// <summary>
        /// Used for high-level dashboard metrics or customer cards without loading full history.
        /// </summary>
        public double GetCurrentBalance(int customerId)
        {
             using var conn = DatabaseHelper.GetConnection();
             return GetLatestBalanceInternal(customerId, conn, null);
        }
    }
}
