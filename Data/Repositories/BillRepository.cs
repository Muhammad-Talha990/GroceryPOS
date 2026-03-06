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
        /// Use this when combining bill saving with other operations (like returns or corrections).
        /// </summary>
        public Bill SaveBillInternal(Bill bill, List<BillDescription> items, SqliteConnection conn, SqliteTransaction txn)
        {
            // ── Step 1: Insert Bill header ──
            using var billCmd = conn.CreateCommand();
            billCmd.Transaction = txn;
            billCmd.CommandText = @"
                INSERT INTO Bill (BillDateTime, SubTotal, DiscountAmount, TaxAmount, GrandTotal, CashReceived, ChangeGiven, UserId, Status, ReferenceBillId, Type, ParentBillId, CustomerId, IsPrinted, PrintedAt, PrintAttempts, PaidAmount, RemainingAmount, PaymentStatus)
                VALUES (@dt, @sub, @disc, @tax, @grand, @cash, @change, @uid, @status, @refId, @type, @parentId, @cid, @isPrinted, @printedAt, @printAttempts, @paid, @remaining, @payStatus);
                SELECT last_insert_rowid();
            ";
            billCmd.Parameters.AddWithValue("@dt", bill.BillDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
            billCmd.Parameters.AddWithValue("@sub", bill.SubTotal);
            billCmd.Parameters.AddWithValue("@disc", bill.DiscountAmount);
            billCmd.Parameters.AddWithValue("@tax", bill.TaxAmount);
            billCmd.Parameters.AddWithValue("@grand", bill.GrandTotal);
            billCmd.Parameters.AddWithValue("@cash", bill.CashReceived);
            billCmd.Parameters.AddWithValue("@change", bill.ChangeGiven);
            billCmd.Parameters.AddWithValue("@uid", bill.UserId.HasValue ? (object)bill.UserId.Value : DBNull.Value);
            billCmd.Parameters.AddWithValue("@status", bill.Status ?? "Completed");
            billCmd.Parameters.AddWithValue("@refId", bill.ReferenceBillId.HasValue ? (object)bill.ReferenceBillId.Value : DBNull.Value);
            billCmd.Parameters.AddWithValue("@type", bill.Type ?? "Sale");
            billCmd.Parameters.AddWithValue("@parentId", bill.ParentBillId.HasValue ? (object)bill.ParentBillId.Value : DBNull.Value);
            billCmd.Parameters.AddWithValue("@cid", bill.CustomerId.HasValue ? (object)bill.CustomerId.Value : DBNull.Value);
            billCmd.Parameters.AddWithValue("@isPrinted", bill.IsPrinted ? 1 : 0);
            billCmd.Parameters.AddWithValue("@printedAt", bill.PrintedAt.HasValue ? (object)bill.PrintedAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
            billCmd.Parameters.AddWithValue("@printAttempts", bill.PrintAttempts);
            billCmd.Parameters.AddWithValue("@paid",      bill.PaidAmount);
            billCmd.Parameters.AddWithValue("@remaining", bill.RemainingAmount);
            billCmd.Parameters.AddWithValue("@payStatus", bill.PaymentStatus ?? "Paid");

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

            bill.Items = items;
            AppLogger.Info($"Bill internal save: ID={bill.BillId} | Status={bill.Status}");
            return bill;
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
            cmd.CommandText = @"
                SELECT b.*, u.Username, u.FullName, u.Role, u.IsActive, u.CreatedAt, 
                       c.Name as CustomerName, c.PrimaryPhone, c.Address as CustomerAddress, c.CreatedAt as CustomerJoinDate
                FROM Bill b
                LEFT JOIN User u ON b.UserId = u.Id
                LEFT JOIN Customers c ON b.CustomerId = c.CustomerId
                WHERE b.bill_id = @id;";
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
                SELECT b.*, u.Username, u.FullName, u.Role, u.IsActive, u.CreatedAt,
                       c.Name as CustomerName, c.PrimaryPhone, c.Address as CustomerAddress, c.CreatedAt as CustomerJoinDate
                FROM Bill b
                LEFT JOIN User u ON b.UserId = u.Id
                LEFT JOIN Customers c ON b.CustomerId = c.CustomerId
                ORDER BY b.BillDateTime DESC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var bill = MapBill(reader);
                bills.Add(bill);
            }

            // Load items for each bill in a single batch
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
                SELECT b.*, u.Username, u.FullName, u.Role, u.IsActive, u.CreatedAt,
                       c.Name as CustomerName, c.PrimaryPhone, c.Address as CustomerAddress, c.CreatedAt as CustomerJoinDate
                FROM Bill b 
                LEFT JOIN User u ON b.UserId = u.Id
                LEFT JOIN Customers c ON b.CustomerId = c.CustomerId
                WHERE b.BillDateTime >= @from AND b.BillDateTime < @to 
                ORDER BY b.BillDateTime DESC;
            ";
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                bills.Add(MapBill(reader));

            // Load items for each bill in a single batch
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

        /// <summary>Updates the status of a bill.</summary>
        public void UpdateBillStatus(int billId, string status, SqliteConnection? conn = null, SqliteTransaction? txn = null)
        {
            bool manageConnection = (conn == null);
            if (manageConnection) conn = DatabaseHelper.GetConnection();

            try
            {
                using var cmd = conn!.CreateCommand();
                if (txn != null) cmd.Transaction = txn;
                cmd.CommandText = "UPDATE Bill SET Status = @status WHERE bill_id = @id;";
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
            var bills = new List<Bill>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT b.*, u.Username, u.FullName, u.Role, u.IsActive, u.CreatedAt,
                       c.Name as CustomerName, c.PrimaryPhone, c.Address as CustomerAddress, c.CreatedAt as CustomerJoinDate
                FROM Bill b
                LEFT JOIN User u ON b.UserId = u.Id
                LEFT JOIN Customers c ON b.CustomerId = c.CustomerId
                WHERE b.ParentBillId = @pid
                ORDER BY b.BillDateTime ASC;";
            cmd.Parameters.AddWithValue("@pid", parentId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var bill = MapBill(reader);
                bills.Add(bill);
            }

            // Load items for each bill in a single batch
            LoadLineItems(conn, bills);

            return bills;
        }

        public List<Bill> GetBillsByCustomerId(int customerId)
        {
            var bills = new List<Bill>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT b.*, u.Username, u.FullName, u.Role, u.IsActive, u.CreatedAt,
                       c.Name as CustomerName, c.PrimaryPhone, c.Address as CustomerAddress, c.CreatedAt as CustomerJoinDate
                FROM Bill b
                LEFT JOIN User u ON b.UserId = u.Id
                LEFT JOIN Customers c ON b.CustomerId = c.CustomerId
                WHERE b.CustomerId = @cid AND b.Type = 'Sale'
                ORDER BY b.BillDateTime DESC;";
            cmd.Parameters.AddWithValue("@cid", customerId);

            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var bill = MapBill(reader);
                    bills.Add(bill);
                }
            }

            // Load items for each bill in one batch
            LoadLineItems(conn, bills);

            return bills;
        }

        public Bill? GetLatestBillByCustomerId(int customerId)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT b.*, u.Username, u.FullName, u.Role, u.IsActive, u.CreatedAt,
                       c.Name as CustomerName, c.PrimaryPhone, c.Address as CustomerAddress, c.CreatedAt as CustomerJoinDate
                FROM Bill b
                LEFT JOIN User u ON b.UserId = u.Id
                LEFT JOIN Customers c ON b.CustomerId = c.CustomerId
                WHERE b.CustomerId = @cid AND b.Type = 'Sale'
                ORDER BY b.BillDateTime DESC LIMIT 1;";
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
                SELECT COUNT(*), COALESCE(SUM(GrandTotal), 0)
                FROM Bill 
                WHERE CustomerId = @cid AND Type = 'Sale'";
            cmd.Parameters.AddWithValue("@cid", customerId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return (reader.GetInt32(0), reader.GetDouble(1));
            }
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
                    Quantity        = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("Quantity"))),
                    UnitPrice       = reader.GetDouble(reader.GetOrdinal("UnitPrice")),
                    TotalPrice      = reader.GetDouble(reader.GetOrdinal("TotalPrice")),
                    ItemDescription = reader.GetString(reader.GetOrdinal("ItemDesc"))
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
                SELECT bd.*, COALESCE(i.Description, 'Unknown Item') AS ItemDesc
                FROM BillDescription bd
                LEFT JOIN Item i ON bd.ItemId = i.itemId
                WHERE bd.Bill_id IN ({billIds});
            ";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var billId = reader.GetInt32(reader.GetOrdinal("Bill_id"));
                if (billDict.TryGetValue(billId, out var bill))
                {
                    bill.Items.Add(new BillDescription
                    {
                        Id              = reader.GetInt32(reader.GetOrdinal("id")),
                        BillId          = billId,
                        ItemId          = reader.GetString(reader.GetOrdinal("ItemId")),
                        Quantity        = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("Quantity"))),
                        UnitPrice       = reader.GetDouble(reader.GetOrdinal("UnitPrice")),
                        TotalPrice      = reader.GetDouble(reader.GetOrdinal("TotalPrice")),
                        ItemDescription = reader.GetString(reader.GetOrdinal("ItemDesc"))
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
                BillId         = reader.GetInt32(reader.GetOrdinal("bill_id")),
                BillDateTime   = DateTime.TryParse(reader.GetString(reader.GetOrdinal("BillDateTime")), out var bdt) ? bdt : DateTime.MinValue,
                SubTotal       = reader.GetDouble(reader.GetOrdinal("SubTotal")),
                DiscountAmount = reader.IsDBNull(reader.GetOrdinal("DiscountAmount")) ? 0 : reader.GetDouble(reader.GetOrdinal("DiscountAmount")),
                TaxAmount      = reader.IsDBNull(reader.GetOrdinal("TaxAmount")) ? 0 : reader.GetDouble(reader.GetOrdinal("TaxAmount")),
                GrandTotal     = reader.GetDouble(reader.GetOrdinal("GrandTotal")),
                CashReceived   = reader.IsDBNull(reader.GetOrdinal("CashReceived")) ? 0 : reader.GetDouble(reader.GetOrdinal("CashReceived")),
                ChangeGiven    = reader.IsDBNull(reader.GetOrdinal("ChangeGiven")) ? 0 : reader.GetDouble(reader.GetOrdinal("ChangeGiven")),
                UserId         = reader.IsDBNull(reader.GetOrdinal("UserId")) ? null : reader.GetInt32(reader.GetOrdinal("UserId")),
                Status         = reader.IsDBNull(reader.GetOrdinal("Status")) ? "Completed" : reader.GetString(reader.GetOrdinal("Status")),
                ReferenceBillId = reader.IsDBNull(reader.GetOrdinal("ReferenceBillId")) ? null : reader.GetInt32(reader.GetOrdinal("ReferenceBillId")),
                Type           = reader.IsDBNull(reader.GetOrdinal("Type")) ? "Sale" : reader.GetString(reader.GetOrdinal("Type")),
                ParentBillId   = reader.IsDBNull(reader.GetOrdinal("ParentBillId")) ? null : (int?)reader.GetInt32(reader.GetOrdinal("ParentBillId")),
                CustomerId     = reader.IsDBNull(reader.GetOrdinal("CustomerId")) ? null : (int?)reader.GetInt32(reader.GetOrdinal("CustomerId")),
                IsPrinted      = reader.GetInt32(reader.GetOrdinal("IsPrinted")) != 0,
                PrintedAt      = reader.IsDBNull(reader.GetOrdinal("PrintedAt")) ? null : (DateTime?)DateTime.Parse(reader.GetString(reader.GetOrdinal("PrintedAt"))),
                PrintAttempts  = reader.GetInt32(reader.GetOrdinal("PrintAttempts")),
                PaidAmount      = reader.HasColumn("PaidAmount")      && !reader.IsDBNull(reader.GetOrdinal("PaidAmount"))      ? reader.GetDouble(reader.GetOrdinal("PaidAmount"))      : 0,
                RemainingAmount = reader.HasColumn("RemainingAmount") && !reader.IsDBNull(reader.GetOrdinal("RemainingAmount")) ? reader.GetDouble(reader.GetOrdinal("RemainingAmount")) : 0,
                PaymentStatus   = reader.HasColumn("PaymentStatus")   && !reader.IsDBNull(reader.GetOrdinal("PaymentStatus"))   ? reader.GetString(reader.GetOrdinal("PaymentStatus"))   : "Paid"
            };

            if (bill.CustomerId.HasValue && reader.HasColumn("CustomerName") && !reader.IsDBNull(reader.GetOrdinal("CustomerName")))
            {
                bill.Customer = new Customer
                {
                    CustomerId = bill.CustomerId.Value,
                    Name = reader.GetString(reader.GetOrdinal("CustomerName")),
                    PrimaryPhone = reader.HasColumn("PrimaryPhone") ? reader.GetString(reader.GetOrdinal("PrimaryPhone")) : string.Empty,
                    Address = reader.HasColumn("CustomerAddress") && !reader.IsDBNull(reader.GetOrdinal("CustomerAddress")) ? reader.GetString(reader.GetOrdinal("CustomerAddress")) : null,
                    CreatedAt = reader.HasColumn("CustomerJoinDate") && DateTime.TryParse(reader.GetString(reader.GetOrdinal("CustomerJoinDate")), out var joinDt) ? joinDt : DateTime.Now
                };
            }


            if (bill.UserId.HasValue && !reader.IsDBNull(reader.GetOrdinal("Username")))
            {
                bill.User = new User
                {
                    Id       = bill.UserId.Value,
                    Username = reader.GetString(reader.GetOrdinal("Username")),
                    FullName = reader.GetString(reader.GetOrdinal("FullName")),
                    Role     = reader.GetString(reader.GetOrdinal("Role")),
                    IsActive = reader.GetInt32(reader.GetOrdinal("IsActive")) != 0,
                    CreatedAt = DateTime.TryParse(reader.GetString(reader.GetOrdinal("CreatedAt")), out var dt) ? dt : DateTime.Now
                };
            }

            return bill;
        }

        public void UpdatePrintStatus(int billId, bool isPrinted, DateTime? printedAt, int printAttempts)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Bill 
                SET IsPrinted = @isPrinted, 
                    PrintedAt = @printedAt, 
                    PrintAttempts = @printAttempts 
                WHERE bill_id = @id;";
            cmd.Parameters.AddWithValue("@isPrinted", isPrinted ? 1 : 0);
            cmd.Parameters.AddWithValue("@printedAt", printedAt.HasValue ? (object)printedAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
            cmd.Parameters.AddWithValue("@printAttempts", printAttempts);
            cmd.Parameters.AddWithValue("@id", billId);
            cmd.ExecuteNonQuery();
        }

        public List<Bill> GetPendingPrintBills()
        {
            var bills = new List<Bill>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT b.*, u.Username, u.FullName, u.Role, u.IsActive, u.CreatedAt,
                       c.Name as CustomerName, c.PrimaryPhone, c.Address as CustomerAddress, c.CreatedAt as CustomerJoinDate
                FROM Bill b
                LEFT JOIN User u ON b.UserId = u.Id
                LEFT JOIN Customers c ON b.CustomerId = c.CustomerId
                WHERE b.IsPrinted = 0 AND b.Status != 'Cancelled'
                ORDER BY b.BillDateTime DESC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                bills.Add(MapBill(reader));

            // Load items for each bill in a single batch
            LoadLineItems(conn, bills);

            return bills;
        }

        // ────────────────────────────────────────────
        //  CREDIT / UDHAR read operations
        // ────────────────────────────────────────────

        /// <summary>Returns all credit bills (RemainingAmount > 0) for a customer, newest first.</summary>
        public List<Bill> GetCreditBillsByCustomer(int customerId)
        {
            var bills = new List<Bill>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT b.*, u.Username, u.FullName, u.Role, u.IsActive, u.CreatedAt,
                       c.Name as CustomerName, c.PrimaryPhone, c.Address as CustomerAddress, c.CreatedAt as CustomerJoinDate
                FROM Bill b
                LEFT JOIN User u ON b.UserId = u.Id
                LEFT JOIN Customers c ON b.CustomerId = c.CustomerId
                WHERE b.CustomerId = @cid
                  AND b.RemainingAmount > 0
                  AND b.Type = 'Sale'
                  AND b.Status != 'Cancelled'
                ORDER BY b.BillDateTime DESC;";
            cmd.Parameters.AddWithValue("@cid", customerId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var bill = MapBill(reader);
                bills.Add(bill);
            }
            LoadLineItems(conn, bills);

            return bills;
        }

        /// <summary>
        /// Returns all bills for a customer for the ledger view (all statuses, Sale type only).
        /// </summary>
        public List<Bill> GetLedgerByCustomer(int customerId)
        {
            var bills = new List<Bill>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT b.*, u.Username, u.FullName, u.Role, u.IsActive, u.CreatedAt,
                       c.Name as CustomerName, c.PrimaryPhone, c.Address as CustomerAddress, c.CreatedAt as CustomerJoinDate
                FROM Bill b
                LEFT JOIN User u ON b.UserId = u.Id
                LEFT JOIN Customers c ON b.CustomerId = c.CustomerId
                WHERE b.CustomerId = @cid AND b.Type = 'Sale'
                ORDER BY b.BillDateTime DESC;";
            cmd.Parameters.AddWithValue("@cid", customerId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                bills.Add(MapBill(reader));

            LoadLineItems(conn, bills);

            return bills;
        }
    }
}
