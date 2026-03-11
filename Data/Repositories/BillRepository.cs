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
                var result = SaveBillInternal(bill, items, conn, txn);
                txn.Commit();
                return result;
            }
            catch (Exception ex)
            {
                txn.Rollback();
                AppLogger.Error("Bill transaction failed — rolled back", ex);
                throw;
            }
        }

        /// <summary>
        /// Saves a bill within an existing transaction. 
        /// </summary>
        public Bill SaveBillInternal(Bill bill, List<BillDescription> items, SqliteConnection conn, SqliteTransaction txn)
        {
            // ── Step 1: Insert Bill header ──
            using var billCmd = conn.CreateCommand();
            billCmd.Transaction = txn;
            billCmd.CommandText = @"
                INSERT INTO Bills (CustomerId, UserId, TaxAmount, DiscountAmount, Status, BillPaymentMethod)
                VALUES (@cid, @uid, @tax, @disc, @status, @billPayMethod);
                SELECT last_insert_rowid();
            ";
            billCmd.Parameters.AddWithValue("@cid", bill.CustomerId.HasValue ? (object)bill.CustomerId.Value : DBNull.Value);
            billCmd.Parameters.AddWithValue("@uid", bill.UserId.HasValue ? (object)bill.UserId.Value : DBNull.Value);
            billCmd.Parameters.AddWithValue("@tax", bill.TaxAmount);
            billCmd.Parameters.AddWithValue("@disc", bill.DiscountAmount);
            billCmd.Parameters.AddWithValue("@status", bill.Status ?? "Completed");
            billCmd.Parameters.AddWithValue("@billPayMethod", bill.PaymentMethod ?? "Cash");

            bill.BillId = Convert.ToInt32(billCmd.ExecuteScalar());

            // ── Step 2: Insert BillItems and Record Stock Changes ──
            foreach (var item in items)
            {
                item.BillId = bill.BillId;

                using var itemCmd = conn.CreateCommand();
                itemCmd.Transaction = txn;
                itemCmd.CommandText = @"
                    INSERT INTO BillItems (BillId, ItemId, Quantity, UnitPrice, DiscountAmount)
                    VALUES (@billId, @itemId, @qty, @price, @itemDisc);
                    
                    INSERT INTO InventoryLogs (ItemId, QuantityChange, ChangeType)
                    VALUES (@itemId, @qtyLog, 'Sale');
                ";
                itemCmd.Parameters.AddWithValue("@billId", item.BillId);
                itemCmd.Parameters.AddWithValue("@itemId", item.ItemInternalId);
                itemCmd.Parameters.AddWithValue("@qty", item.Quantity);
                itemCmd.Parameters.AddWithValue("@qtyLog", -item.Quantity); // Deduction
                itemCmd.Parameters.AddWithValue("@price", item.UnitPrice);
                itemCmd.Parameters.AddWithValue("@itemDisc", item.DiscountAmount);
                itemCmd.ExecuteNonQuery();
            }

            // ── Step 3: Record Payment ──
            if (bill.PaidAmount > 0)
            {
                using var payCmd = conn.CreateCommand();
                payCmd.Transaction = txn;
                payCmd.CommandText = @"
                    INSERT INTO Payments (BillId, Amount, PaymentMethod, TransactionType)
                    VALUES (@billId, @amount, @payMethod, 'Sale');
                ";
                payCmd.Parameters.AddWithValue("@billId", bill.BillId);
                payCmd.Parameters.AddWithValue("@amount", bill.PaidAmount);
                payCmd.Parameters.AddWithValue("@payMethod", bill.PaymentMethod ?? "Cash");
                payCmd.ExecuteNonQuery();
            }

            bill.Items = items;
            AppLogger.Info($"3NF Bill saved: ID={bill.BillId} | Status={bill.Status} | Paid={bill.PaidAmount:N2}");
            return bill;
        }

        // ────────────────────────────────────────────
        //  READ operations
        // ────────────────────────────────────────────

        /// <summary>Gets a bill by ID including its line items.</summary>
        public Bill? GetById(int billId)
        {
            using var conn = DatabaseHelper.GetConnection();

            // Get bill header with calculated aggregates
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT b.*, 
                       u.Username, u.FullName as UserFullName, u.Role as UserRole,
                       c.FullName as CustomerName, c.Phone as CustomerPhone, c.Address as CustomerAddress,
                       (SELECT COALESCE(SUM(Quantity * UnitPrice), 0) FROM BillItems WHERE BillId = b.BillId) as SubTotal,
                       (SELECT COALESCE(SUM(Amount), 0) FROM Payments WHERE BillId = b.BillId) as PaidAmount,
                       (SELECT PaymentMethod FROM Payments WHERE BillId = b.BillId ORDER BY PaidAt ASC LIMIT 1) as PaymentMethod
                FROM Bills b
                LEFT JOIN Users u ON b.UserId = u.Id
                LEFT JOIN Customers c ON b.CustomerId = c.CustomerId
                WHERE b.BillId = @id;";
            cmd.Parameters.AddWithValue("@id", billId);

            Bill? bill = null;
            using (var reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                    bill = MapBill(reader);
            }

            if (bill != null)
                LoadLineItems(conn, new List<Bill> { bill });

            return bill;
        }

        /// <summary>Gets all bills ordered by date descending.</summary>
        public List<Bill> GetAll()
        {
            var bills = new List<Bill>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT b.*, 
                       u.Username, u.FullName as UserFullName, u.Role as UserRole,
                       c.FullName as CustomerName, c.Phone as CustomerPhone, c.Address as CustomerAddress,
                       (SELECT COALESCE(SUM(Quantity * UnitPrice), 0) FROM BillItems WHERE BillId = b.BillId) as SubTotal,
                       (SELECT COALESCE(SUM(Amount), 0) FROM Payments WHERE BillId = b.BillId) as PaidAmount,
                       (SELECT PaymentMethod FROM Payments WHERE BillId = b.BillId ORDER BY PaidAt ASC LIMIT 1) as PaymentMethod
                FROM Bills b
                LEFT JOIN Users u ON b.UserId = u.Id
                LEFT JOIN Customers c ON b.CustomerId = c.CustomerId
                ORDER BY b.CreatedAt DESC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                bills.Add(MapBill(reader));

            LoadLineItems(conn, bills);
            return bills;
        }

        /// <summary>Gets bills within a date range.</summary>
        public List<Bill> GetByDateRange(DateTime from, DateTime to)
        {
            var bills = new List<Bill>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT b.*, 
                       u.Username, u.FullName as UserFullName, u.Role as UserRole,
                       c.FullName as CustomerName, c.Phone as CustomerPhone, c.Address as CustomerAddress,
                       (SELECT COALESCE(SUM(Quantity * UnitPrice), 0) FROM BillItems WHERE BillId = b.BillId) as SubTotal,
                       (SELECT COALESCE(SUM(Amount), 0) FROM Payments WHERE BillId = b.BillId) as PaidAmount,
                       (SELECT PaymentMethod FROM Payments WHERE BillId = b.BillId ORDER BY PaidAt ASC LIMIT 1) as PaymentMethod
                FROM Bills b
                LEFT JOIN Users u ON b.UserId = u.Id
                LEFT JOIN Customers c ON b.CustomerId = c.CustomerId
                WHERE b.CreatedAt >= @from AND b.CreatedAt < @to 
                ORDER BY b.CreatedAt DESC;";
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                bills.Add(MapBill(reader));

            LoadLineItems(conn, bills);
            return bills;
        }

        /// <summary>Gets today's bills.</summary>
        public List<Bill> GetToday()
        {
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);
            return GetByDateRange(today, tomorrow);
        }

        /// <summary>Gets today's total revenue (Sales Value).</summary>
        public double GetTodayTotal()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(bi.Quantity * bi.UnitPrice) + b.TaxAmount - b.DiscountAmount, 0)
                FROM Bills b
                JOIN BillItems bi ON b.BillId = bi.BillId
                WHERE b.CreatedAt >= @from AND b.CreatedAt < @to AND b.Status != 'Cancelled';";
            cmd.Parameters.AddWithValue("@from", DateTime.Today.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", DateTime.Today.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss"));
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        /// <summary>Gets today's total remaining due amount (credit/udhar).</summary>
        public double GetTodayTotalRemaining()
        {
            return GetTodayTotal() - GetTodayTotalPaidInSales();
        }

        private double GetTodayTotalPaidInSales()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(p.Amount), 0)
                FROM Payments p
                JOIN Bills b ON p.BillId = b.BillId
                WHERE b.CreatedAt >= @from AND b.CreatedAt < @to 
                  AND b.Status != 'Cancelled';";
            cmd.Parameters.AddWithValue("@from", DateTime.Today.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", DateTime.Today.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss"));
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        /// <summary>Gets today's cash collected from today's sale bills only (excludes credit recovery and return offsets).</summary>
        public double GetTodayTotalPaid()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(p.Amount), 0)
                FROM Payments p
                JOIN Bills b ON p.BillId = b.BillId
                WHERE p.PaidAt >= @from AND p.PaidAt < @to
                  AND b.CreatedAt >= @from AND b.CreatedAt < @to
                  AND b.Status != 'Cancelled'
                  AND p.TransactionType != 'Return Offset';";
            cmd.Parameters.AddWithValue("@from", DateTime.Today.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", DateTime.Today.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss"));
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        /// <summary>Gets today's total cash refunded to customers from returns.</summary>
        public double GetTodayCashRefunded()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(RefundAmount), 0) FROM BillReturns 
                WHERE ReturnedAt >= @from AND ReturnedAt < @to;";
            cmd.Parameters.AddWithValue("@from", DateTime.Today.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", DateTime.Today.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss"));
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        /// <summary>Gets today's recovered credit (genuine payments made today for previous bills, excluding return offsets).</summary>
        public double GetTodayRecoveredCredit()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(p.Amount), 0)
                FROM Payments p
                JOIN Bills b ON p.BillId = b.BillId
                WHERE p.PaidAt >= @from AND p.PaidAt < @to
                  AND b.CreatedAt < @from
                  AND p.TransactionType != 'Return Offset';";
            cmd.Parameters.AddWithValue("@from", DateTime.Today.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", DateTime.Today.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss"));
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        /// <summary>Gets the absolute total value of all return transactions today (cash refunds + credit offsets).</summary>
        public double GetTodayReturnsTotal()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(bri.Quantity * bri.UnitPrice), 0)
                FROM BillReturnItems bri
                JOIN BillReturns br ON bri.ReturnId = br.ReturnId
                WHERE br.ReturnedAt >= @from AND br.ReturnedAt < @to;";
            cmd.Parameters.AddWithValue("@from", DateTime.Today.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", DateTime.Today.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss"));
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        /// <summary>Gets today's net cash in drawer: cash payments received minus cash refunds given for returns.</summary>
        public double GetTodayCashInDrawer()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT 
                    COALESCE((SELECT SUM(p.Amount)
                        FROM Payments p
                        JOIN Bills b ON p.BillId = b.BillId
                        WHERE p.PaidAt >= @from AND p.PaidAt < @to
                          AND b.Status != 'Cancelled'
                          AND p.TransactionType != 'Return Offset'
                          AND b.BillPaymentMethod = 'Cash'), 0)
                    -
                    COALESCE((SELECT SUM(RefundAmount) FROM BillReturns 
                        WHERE ReturnedAt >= @from AND ReturnedAt < @to), 0);";
            cmd.Parameters.AddWithValue("@from", DateTime.Today.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", DateTime.Today.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss"));
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        /// <summary>Gets today's payments received for Online bills only (sale payments + credit recovery, excludes return offsets).</summary>
        public double GetTodayOnlinePayments()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(p.Amount), 0)
                FROM Payments p
                JOIN Bills b ON p.BillId = b.BillId
                WHERE p.PaidAt >= @from AND p.PaidAt < @to
                  AND b.Status != 'Cancelled'
                  AND p.TransactionType != 'Return Offset'
                  AND b.BillPaymentMethod = 'Online';";
            cmd.Parameters.AddWithValue("@from", DateTime.Today.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", DateTime.Today.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss"));
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        /// <summary>Gets total return value for a date range.</summary>
        public double GetReturnsTotalByDateRange(DateTime from, DateTime to)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(RefundAmount), 0) FROM BillReturns
                WHERE ReturnedAt >= @from AND ReturnedAt < @to;";
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        /// <summary>Gets sale bills only (no returns) for a date range.</summary>
        public List<Bill> GetSalesOnlyByDateRange(DateTime from, DateTime to)
        {
            // In 3NF, all entries in Bills are sales. Returns are separate.
            return GetByDateRange(from, to);
        }

        /// <summary>Gets total outstanding credit across all customers.</summary>
        public double GetOutstandingCreditTotal()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT 
                    (SELECT COALESCE(SUM(bi.Quantity * bi.UnitPrice) + b.TaxAmount - b.DiscountAmount, 0)
                     FROM Bills b JOIN BillItems bi ON b.BillId = bi.BillId
                     WHERE b.Status != 'Cancelled')
                    -
                    (SELECT COALESCE(SUM(Amount), 0) FROM Payments);";
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        /// <summary>Gets today's bill count.</summary>
        public int GetTodayCount()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Bills WHERE CreatedAt >= @from AND CreatedAt < @to;";
            cmd.Parameters.AddWithValue("@from", DateTime.Today.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", DateTime.Today.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss"));
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>Gets the next available bill ID.</summary>
        public int GetNextBillId()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(MAX(BillId), 0) + 1 FROM Bills;";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>Updates the status of a bill.</summary>
        public void UpdateBillStatus(int billId, string status, SqliteConnection? conn = null, SqliteTransaction? txn = null)
        {
            bool manageConnection = (conn == null);
            if (manageConnection) conn = DatabaseHelper.GetConnection();

            try
            {
                using var cmd = conn!.CreateCommand();
                if (txn != null) cmd.Transaction = txn;
                cmd.CommandText = "UPDATE Bills SET Status = @status WHERE BillId = @id;";
                cmd.Parameters.AddWithValue("@status", status);
                cmd.Parameters.AddWithValue("@id", billId);
                cmd.ExecuteNonQuery();
            }
            finally
            {
                if (manageConnection) conn?.Dispose();
            }
        }

        public List<Bill> GetReturnsByParentId(int parentId)
        {
            // New 3NF logic: returns have their own table, we can wrap them as virtual 'Bill' objects for UI
            var bills = new List<Bill>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT r.ReturnId as BillId, r.ReturnedAt as CreatedAt, r.BillId as ParentId,
                       (SELECT UserId FROM Bills WHERE BillId = r.BillId) as UserId,
                       r.RefundAmount as TotalAmount
                FROM BillReturns r
                WHERE r.BillId = @pid
                ORDER BY r.ReturnedAt ASC;";
            cmd.Parameters.AddWithValue("@pid", parentId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                bills.Add(new Bill
                {
                    BillId = reader.GetInt32(0),
                    CreatedAt = reader.GetDateTime(1),
                    SubTotal = -reader.GetDouble(4), // Return value is negative
                    PaidAmount = reader.GetDouble(4), // Refunded cash
                    Status = "Completed",
                    Type = "Return",
                    ParentBillId = reader.GetInt32(2)
                });
            }
            return bills;
        }

        public List<Bill> GetBillsByCustomerId(int customerId)
        {
            var bills = new List<Bill>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT b.*, 
                       u.Username, u.FullName as UserFullName, u.Role as UserRole,
                       c.FullName as CustomerName, c.Phone as CustomerPhone, c.Address as CustomerAddress,
                       (SELECT COALESCE(SUM(Quantity * UnitPrice), 0) FROM BillItems WHERE BillId = b.BillId) as SubTotal,
                       (SELECT COALESCE(SUM(Amount), 0) FROM Payments WHERE BillId = b.BillId) as PaidAmount,
                       (SELECT PaymentMethod FROM Payments WHERE BillId = b.BillId ORDER BY PaidAt ASC LIMIT 1) as PaymentMethod
                FROM Bills b
                LEFT JOIN Users u ON b.UserId = u.Id
                LEFT JOIN Customers c ON b.CustomerId = c.CustomerId
                WHERE b.CustomerId = @cid
                ORDER BY b.CreatedAt DESC;";
            cmd.Parameters.AddWithValue("@cid", customerId);

            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                    bills.Add(MapBill(reader));
            }
            LoadLineItems(conn, bills);
            return bills;
        }

        public Bill? GetLatestBillByCustomerId(int customerId)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT b.*, 
                       u.Username, u.FullName as UserFullName, u.Role as UserRole,
                       c.FullName as CustomerName, c.Phone as CustomerPhone, c.Address as CustomerAddress,
                       (SELECT COALESCE(SUM(Quantity * UnitPrice), 0) FROM BillItems WHERE BillId = b.BillId) as SubTotal,
                       (SELECT COALESCE(SUM(Amount), 0) FROM Payments WHERE BillId = b.BillId) as PaidAmount,
                       (SELECT PaymentMethod FROM Payments WHERE BillId = b.BillId ORDER BY PaidAt ASC LIMIT 1) as PaymentMethod
                FROM Bills b
                LEFT JOIN Users u ON b.UserId = u.Id
                LEFT JOIN Customers c ON b.CustomerId = c.CustomerId
                WHERE b.CustomerId = @cid
                ORDER BY b.CreatedAt DESC LIMIT 1;";
            cmd.Parameters.AddWithValue("@cid", customerId);

            Bill? bill = null;
            using (var reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                    bill = MapBill(reader);
            }
            if (bill != null)
                LoadLineItems(conn, new List<Bill> { bill });

            return bill;
        }

        public (int BillCount, double TotalAmount) GetCustomerStats(int customerId)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(b.BillId), 
                       COALESCE(SUM(bi.Quantity * bi.UnitPrice + b.TaxAmount - b.DiscountAmount), 0)
                FROM Bills b
                JOIN BillItems bi ON b.BillId = bi.BillId
                WHERE b.CustomerId = @cid AND b.Status != 'Cancelled'";
            cmd.Parameters.AddWithValue("@cid", customerId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return (reader.GetInt32(0), reader.GetDouble(1));
            return (0, 0);
        }


        // ────────────────────────────────────────────
        //  Helper: get line items for a bill
        // ────────────────────────────────────────────

        private List<BillDescription> GetBillItems(SqliteConnection conn, int billId)
        {
            var items = new List<BillDescription>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT bi.*, i.Barcode, i.Description AS ItemDesc
                FROM BillItems bi
                LEFT JOIN Items i ON bi.ItemId = i.ItemId
                WHERE bi.BillId = @billId;
            ";
            cmd.Parameters.AddWithValue("@billId", billId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var barcodeOrd = reader.GetOrdinal("Barcode");
                items.Add(new BillDescription
                {
                    BillItemId      = reader.GetInt32(reader.GetOrdinal("BillItemId")),
                    BillId          = reader.GetInt32(reader.GetOrdinal("BillId")),
                    ItemInternalId  = reader.GetInt32(reader.GetOrdinal("ItemId")),
                    ItemId          = reader.GetInt32(reader.GetOrdinal("ItemId")).ToString(),
                    Barcode         = reader.IsDBNull(barcodeOrd) ? null : reader.GetString(barcodeOrd),
                    ItemDescription = reader.IsDBNull(reader.GetOrdinal("ItemDesc")) ? "" : reader.GetString(reader.GetOrdinal("ItemDesc")),
                    Quantity        = reader.GetDouble(reader.GetOrdinal("Quantity")),
                    UnitPrice       = reader.GetDouble(reader.GetOrdinal("UnitPrice")),
                    DiscountAmount  = reader.GetDouble(reader.GetOrdinal("DiscountAmount")),
                    TotalPrice      = reader.GetDouble(reader.GetOrdinal("Quantity")) * 
                                     reader.GetDouble(reader.GetOrdinal("UnitPrice")) - 
                                     reader.GetDouble(reader.GetOrdinal("DiscountAmount"))
                });
            }
            return items;
        }
        /// <summary>
        /// Batched load of line items for a list of bills to avoid N+1 query performance issues.
        /// </summary>
        private void LoadLineItems(SqliteConnection conn, List<Bill> bills)
        {
            if (bills == null || !bills.Any()) return;

            var billDict = bills.ToDictionary(b => b.BillId);
            var billIds = string.Join(",", billDict.Keys);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT bi.*, i.Barcode as ItemBarcode, i.Description as ItemName
                FROM BillItems bi
                JOIN Items i ON bi.ItemId = i.ItemId
                WHERE bi.BillId IN ({billIds});";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var billId = reader.GetInt32(reader.GetOrdinal("BillId"));
                if (billDict.TryGetValue(billId, out var bill))
                {
                    var barcodeOrd = reader.GetOrdinal("ItemBarcode");
                    bill.Items.Add(new BillDescription
                    {
                        BillItemId     = reader.GetInt32(reader.GetOrdinal("BillItemId")),
                        BillId         = billId,
                        ItemInternalId = reader.GetInt32(reader.GetOrdinal("ItemId")),
                        ItemId         = reader.GetInt32(reader.GetOrdinal("ItemId")).ToString(),
                        Barcode        = reader.IsDBNull(barcodeOrd) ? null : reader.GetString(barcodeOrd),
                        ItemDescription = reader.IsDBNull(reader.GetOrdinal("ItemName")) ? "" : reader.GetString(reader.GetOrdinal("ItemName")),
                        Quantity       = reader.GetDouble(reader.GetOrdinal("Quantity")),
                        UnitPrice      = reader.GetDouble(reader.GetOrdinal("UnitPrice")),
                        DiscountAmount = reader.GetDouble(reader.GetOrdinal("DiscountAmount")),
                        TotalPrice     = reader.GetDouble(reader.GetOrdinal("Quantity")) * 
                                         reader.GetDouble(reader.GetOrdinal("UnitPrice")) - 
                                         reader.GetDouble(reader.GetOrdinal("DiscountAmount"))
                    });
                }
            }
        }

        // ────────────────────────────────────────────
        //  Mapper
        // ────────────────────────────────────────────

        private static Bill MapBill(SqliteDataReader reader)
        {
            var bill = new Bill
            {
                BillId         = reader.GetInt32(reader.GetOrdinal("BillId")),
                CreatedAt      = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                CustomerId     = reader.IsDBNull(reader.GetOrdinal("CustomerId")) ? null : reader.GetInt32(reader.GetOrdinal("CustomerId")),
                UserId         = reader.IsDBNull(reader.GetOrdinal("UserId"))     ? null : reader.GetInt32(reader.GetOrdinal("UserId")),
                TaxAmount      = reader.GetDouble(reader.GetOrdinal("TaxAmount")),
                DiscountAmount = reader.GetDouble(reader.GetOrdinal("DiscountAmount")),
                Status         = reader.GetString(reader.GetOrdinal("Status")),
                SubTotal       = reader.HasColumn("SubTotal") ? reader.GetDouble(reader.GetOrdinal("SubTotal")) : 0,
                PaidAmount     = reader.HasColumn("PaidAmount") ? reader.GetDouble(reader.GetOrdinal("PaidAmount")) : 0,
                PaymentMethod  = reader.HasColumn("BillPaymentMethod") && !reader.IsDBNull(reader.GetOrdinal("BillPaymentMethod"))
                                 ? reader.GetString(reader.GetOrdinal("BillPaymentMethod"))
                                 : (reader.HasColumn("PaymentMethod") && !reader.IsDBNull(reader.GetOrdinal("PaymentMethod")) ? reader.GetString(reader.GetOrdinal("PaymentMethod")) : "Cash")
            };

            // Mapping Customer navigation
            if (bill.CustomerId.HasValue && reader.HasColumn("CustomerName") && !reader.IsDBNull(reader.GetOrdinal("CustomerName")))
            {
                bill.Customer = new Customer
                {
                    CustomerId   = bill.CustomerId.Value,
                    FullName     = reader.GetString(reader.GetOrdinal("CustomerName")),
                    Phone        = reader.GetString(reader.GetOrdinal("CustomerPhone")),
                    Address      = reader.IsDBNull(reader.GetOrdinal("CustomerAddress")) ? null : reader.GetString(reader.GetOrdinal("CustomerAddress"))
                };
            }

            // Mapping User navigation
            if (bill.UserId.HasValue && reader.HasColumn("Username") && !reader.IsDBNull(reader.GetOrdinal("Username")))
            {
                bill.User = new User
                {
                    Id       = bill.UserId.Value,
                    Username = reader.GetString(reader.GetOrdinal("Username")),
                    FullName = reader.GetString(reader.GetOrdinal("UserFullName")),
                    Role     = reader.GetString(reader.GetOrdinal("UserRole"))
                };
            }

            return bill;
        }

        public void UpdatePrintStatus(int billId, bool isPrinted, DateTime? printedAt, int printAttempts)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Bills 
                SET IsPrinted = @isPrinted, 
                    PrintedAt = @printedAt, 
                    PrintAttempts = @printAttempts 
                WHERE BillId = @id;";
            cmd.Parameters.AddWithValue("@isPrinted", isPrinted ? 1 : 0);
            cmd.Parameters.AddWithValue("@printedAt", printedAt.HasValue ? (object)printedAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
            cmd.Parameters.AddWithValue("@printAttempts", printAttempts);
            cmd.Parameters.AddWithValue("@id", billId);
            cmd.ExecuteNonQuery();
        }

        // ────────────────────────────────────────────
        //  CREDIT / UDHAR read operations
        // ────────────────────────────────────────────

        /// <summary>Returns all credit bills for a customer, newest first.</summary>
        public List<Bill> GetCreditBillsByCustomer(int customerId)
        {
            var allBills = GetBillsByCustomerId(customerId);
            return allBills.Where(b => b.RemainingAmount > 0 && b.Status != "Cancelled").ToList();
        }

        /// <summary>
        /// Returns all bills for a customer for the ledger view (all statuses, Sale type only).
        /// </summary>
        public List<Bill> GetLedgerByCustomer(int customerId)
        {
            return GetBillsByCustomerId(customerId);
        }
    }
}
