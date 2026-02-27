using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using GroceryPOS.Helpers;
using GroceryPOS.Models;

namespace GroceryPOS.Data.Repositories
{
    /// <summary>
    /// Data access for the Bill and BillDescription tables.
    /// Handles transactional bill saving with stock deduction.
    /// </summary>
    public class BillRepository
    {
        private readonly ItemRepository _itemRepo = new();

        // ────────────────────────────────────────────
        //  TRANSACTIONAL BILL SAVE
        // ────────────────────────────────────────────

        /// <summary>
        /// Saves a complete bill atomically:
        ///   1. Insert Bill header
        ///   2. Insert all BillDescription line items
        ///   3. Deduct stock for each item
        /// Rolls back everything if any step fails (including insufficient stock).
        /// </summary>
        /// <returns>The saved Bill with its generated BillId.</returns>
        public Bill SaveBillWithTransaction(Bill bill, List<BillDescription> items)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var txn = conn.BeginTransaction();

            try
            {
                // ── Step 1: Insert Bill header ──
                using var billCmd = conn.CreateCommand();
                billCmd.Transaction = txn;
                billCmd.CommandText = @"
                    INSERT INTO Bill (BillDateTime, SubTotal, DiscountAmount, TaxAmount, GrandTotal, CashReceived, ChangeGiven, UserId)
                    VALUES (@dt, @sub, @disc, @tax, @grand, @cash, @change, @uid);
                    SELECT last_insert_rowid();
                ";
                billCmd.Parameters.AddWithValue("@dt", bill.BillDateTime);
                billCmd.Parameters.AddWithValue("@sub", bill.SubTotal);
                billCmd.Parameters.AddWithValue("@disc", bill.DiscountAmount);
                billCmd.Parameters.AddWithValue("@tax", bill.TaxAmount);
                billCmd.Parameters.AddWithValue("@grand", bill.GrandTotal);
                billCmd.Parameters.AddWithValue("@cash", bill.CashReceived);
                billCmd.Parameters.AddWithValue("@change", bill.ChangeGiven);
                billCmd.Parameters.AddWithValue("@uid", bill.UserId.HasValue ? (object)bill.UserId.Value : DBNull.Value);

                bill.BillId = Convert.ToInt32(billCmd.ExecuteScalar());

                // ── Step 2: Insert BillDescription items ──
                foreach (var item in items)
                {
                    item.BillId = bill.BillId;

                    using var itemCmd = conn.CreateCommand();
                    itemCmd.Transaction = txn;
                    itemCmd.CommandText = @"
                        INSERT INTO BillDescription (Bill_id, ItemId, Quantity, UnitPrice, TotalPrice)
                        VALUES (@billId, @itemId, @qty, @price, @total);
                        
                        UPDATE Item SET StockQuantity = StockQuantity - @qty 
                        WHERE itemId = @itemId;
                    ";
                    itemCmd.Parameters.AddWithValue("@billId", item.BillId);
                    itemCmd.Parameters.AddWithValue("@itemId", item.ItemId);
                    itemCmd.Parameters.AddWithValue("@qty", item.Quantity);
                    itemCmd.Parameters.AddWithValue("@price", item.UnitPrice);
                    itemCmd.Parameters.AddWithValue("@total", item.TotalPrice);
                    itemCmd.ExecuteNonQuery();
                }

                txn.Commit();
                bill.Items = items;

                AppLogger.Info($"Bill saved: ID={bill.BillId} | Total: Rs.{bill.GrandTotal:N2} | Items: {items.Count}");
                return bill;
            }
            catch (Exception ex)
            {
                txn.Rollback();
                AppLogger.Error("Bill transaction failed — rolled back", ex);
                throw;
            }
        }

        // ────────────────────────────────────────────
        //  READ operations
        // ────────────────────────────────────────────

        /// <summary>Gets a bill by ID including its line items.</summary>
        public Bill? GetById(int billId)
        {
            using var conn = DatabaseHelper.GetConnection();

            // Get bill header
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Bill WHERE bill_id = @id;";
            cmd.Parameters.AddWithValue("@id", billId);

            Bill? bill = null;
            using (var reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                    bill = MapBill(reader);
            }

            if (bill != null)
                bill.Items = GetBillItems(conn, bill.BillId);

            return bill;
        }

        /// <summary>Gets all bills ordered by date descending.</summary>
        public List<Bill> GetAll()
        {
            var bills = new List<Bill>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Bill ORDER BY BillDateTime DESC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var bill = MapBill(reader);
                bills.Add(bill);
            }

            // Load items for each bill
            foreach (var bill in bills)
                bill.Items = GetBillItems(conn, bill.BillId);

            return bills;
        }

        /// <summary>Gets bills within a date range.</summary>
        public List<Bill> GetByDateRange(DateTime from, DateTime to)
        {
            var bills = new List<Bill>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT * FROM Bill 
                WHERE BillDateTime >= @from AND BillDateTime < @to 
                ORDER BY BillDateTime DESC;
            ";
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                bills.Add(MapBill(reader));

            foreach (var bill in bills)
                bill.Items = GetBillItems(conn, bill.BillId);

            return bills;
        }

        /// <summary>Gets today's bills.</summary>
        public List<Bill> GetToday()
        {
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);
            return GetByDateRange(today, tomorrow);
        }

        /// <summary>Gets today's total revenue.</summary>
        public double GetTodayTotal()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(GrandTotal), 0) FROM Bill 
                WHERE BillDateTime >= @from AND BillDateTime < @to;
            ";
            cmd.Parameters.AddWithValue("@from", DateTime.Today.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", DateTime.Today.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss"));
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        /// <summary>Gets today's bill count.</summary>
        public int GetTodayCount()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*) FROM Bill 
                WHERE BillDateTime >= @from AND BillDateTime < @to;
            ";
            cmd.Parameters.AddWithValue("@from", DateTime.Today.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", DateTime.Today.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss"));
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>Gets the next available bill ID.</summary>
        public int GetNextBillId()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(MAX(bill_id), 0) + 1 FROM Bill;";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        // ────────────────────────────────────────────
        //  Helper: get line items for a bill
        // ────────────────────────────────────────────

        private List<BillDescription> GetBillItems(SqliteConnection conn, int billId)
        {
            var items = new List<BillDescription>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT bd.*, COALESCE(i.Description, 'Unknown Item') AS ItemDesc
                FROM BillDescription bd
                LEFT JOIN Item i ON bd.ItemId = i.itemId
                WHERE bd.Bill_id = @billId;
            ";
            cmd.Parameters.AddWithValue("@billId", billId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new BillDescription
                {
                    Id              = reader.GetInt32(reader.GetOrdinal("id")),
                    BillId          = reader.GetInt32(reader.GetOrdinal("Bill_id")),
                    ItemId          = reader.GetString(reader.GetOrdinal("ItemId")),
                    Quantity        = reader.GetInt32(reader.GetOrdinal("Quantity")),
                    UnitPrice       = reader.GetDouble(reader.GetOrdinal("UnitPrice")),
                    TotalPrice      = reader.GetDouble(reader.GetOrdinal("TotalPrice")),
                    ItemDescription = reader.GetString(reader.GetOrdinal("ItemDesc"))
                });
            }

            return items;
        }

        // ────────────────────────────────────────────
        //  Mapper
        // ────────────────────────────────────────────

        private static Bill MapBill(SqliteDataReader reader)
        {
            return new Bill
            {
                BillId         = reader.GetInt32(reader.GetOrdinal("bill_id")),
                BillDateTime   = reader.GetString(reader.GetOrdinal("BillDateTime")),
                SubTotal       = reader.GetDouble(reader.GetOrdinal("SubTotal")),
                DiscountAmount = reader.IsDBNull(reader.GetOrdinal("DiscountAmount")) ? 0 : reader.GetDouble(reader.GetOrdinal("DiscountAmount")),
                TaxAmount      = reader.IsDBNull(reader.GetOrdinal("TaxAmount")) ? 0 : reader.GetDouble(reader.GetOrdinal("TaxAmount")),
                GrandTotal     = reader.GetDouble(reader.GetOrdinal("GrandTotal")),
                CashReceived   = reader.IsDBNull(reader.GetOrdinal("CashReceived")) ? 0 : reader.GetDouble(reader.GetOrdinal("CashReceived")),
                ChangeGiven    = reader.IsDBNull(reader.GetOrdinal("ChangeGiven")) ? 0 : reader.GetDouble(reader.GetOrdinal("ChangeGiven")),
                UserId         = reader.IsDBNull(reader.GetOrdinal("UserId")) ? null : reader.GetInt32(reader.GetOrdinal("UserId"))
            };
        }
    }
}
