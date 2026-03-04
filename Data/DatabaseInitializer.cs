using System;
using Microsoft.Data.Sqlite;
using GroceryPOS.Helpers;

namespace GroceryPOS.Data
{
    /// <summary>
    /// Creates the database schema (4 tables: User, Item, Bill, BillDescription)
    /// and seeds default data on first run.
    /// </summary>
    public static class DatabaseInitializer
    {
        /// <summary>
        /// Ensures all tables, indexes, and seed data exist.
        /// Safe to call on every application startup.
        /// </summary>
        public static void Initialize()
        {
            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    // ── Enable WAL for better performance ──
                    Execute(conn, "PRAGMA journal_mode = WAL;");

                    // ══════════════════════════════════════════
                    //  TABLE 1: User (authentication)
                    // ══════════════════════════════════════════
                    Execute(conn, @"
                        CREATE TABLE IF NOT EXISTS User (
                            Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                            Username    TEXT    NOT NULL UNIQUE,
                            PasswordHash TEXT   NOT NULL,
                            FullName    TEXT    NOT NULL,
                            Role        TEXT    NOT NULL DEFAULT 'Cashier',
                            IsActive    INTEGER NOT NULL DEFAULT 1,
                            CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
                        );
                    ");

                    // ══════════════════════════════════════════
                    //  TABLE 2: Item (inventory)
                    // ══════════════════════════════════════════
                    Execute(conn, @"
                        CREATE TABLE IF NOT EXISTS Item (
                            itemId          TEXT    PRIMARY KEY,
                            Description     TEXT    NOT NULL,
                            CostPrice       REAL    NOT NULL,
                            SalePrice       REAL    NOT NULL,
                            ItemCategory    TEXT,
                            StockQuantity   REAL    NOT NULL DEFAULT 0,
                            MinStockThreshold REAL NOT NULL DEFAULT 10
                        );
                    ");

                    // ══════════════════════════════════════════
                    //  TABLE 3: Bill (sale header)
                    // ══════════════════════════════════════════
                    Execute(conn, @"
                        CREATE TABLE IF NOT EXISTS Bill (
                            bill_id         INTEGER PRIMARY KEY AUTOINCREMENT,
                            BillDateTime    TEXT    NOT NULL,
                            SubTotal        REAL    NOT NULL,
                            DiscountAmount  REAL    DEFAULT 0,
                            TaxAmount       REAL    DEFAULT 0,
                            GrandTotal      REAL    NOT NULL,
                            CashReceived    REAL,
                            ChangeGiven     REAL,
                            UserId          INTEGER,
                            CustomerId      INTEGER,
                            FOREIGN KEY (UserId) REFERENCES User(Id),
                            FOREIGN KEY (CustomerId) REFERENCES Customers(CustomerId)
                        );
                    ");

                    // ══════════════════════════════════════════
                    //  TABLE 4: BillDescription (sale line items)
                    // ══════════════════════════════════════════
                    Execute(conn, @"
                        CREATE TABLE IF NOT EXISTS BillDescription (
                            id          INTEGER PRIMARY KEY AUTOINCREMENT,
                            Bill_id     INTEGER NOT NULL,
                            ItemId      TEXT    NOT NULL,
                            Quantity    REAL    NOT NULL,
                            UnitPrice   REAL    NOT NULL,
                            TotalPrice  REAL    NOT NULL,
                            FOREIGN KEY (Bill_id) REFERENCES Bill(bill_id) ON DELETE CASCADE,
                            FOREIGN KEY (ItemId)  REFERENCES Item(itemId)  ON DELETE RESTRICT
                        );
                    ");

                    // ══════════════════════════════════════════
                    //  TABLE 6: stock (Purchase Ledger / Supply History)
                    // ══════════════════════════════════════════
                    Execute(conn, @"
                        CREATE TABLE IF NOT EXISTS stock (
                            Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                            product_id      TEXT    NOT NULL,
                            bill_id         TEXT    NOT NULL,
                            quantity        INTEGER NOT NULL,
                            system_date     TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
                            image_path      TEXT
                        );
                    ");

                    // ══════════════════════════════════════════
                    //  TABLE 8: Customers (customer management)
                    // ══════════════════════════════════════════
                    Execute(conn, @"
                        CREATE TABLE IF NOT EXISTS Customers (
                            CustomerId      INTEGER PRIMARY KEY AUTOINCREMENT,
                            Name            TEXT    NOT NULL,
                            PrimaryPhone    TEXT    NOT NULL UNIQUE,
                            SecondaryPhone  TEXT,
                            Address         TEXT,
                            CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
                        );

                        CREATE INDEX IF NOT EXISTS IX_Customers_Name ON Customers(Name);
                        CREATE INDEX IF NOT EXISTS IX_Customers_SecondaryPhone ON Customers(SecondaryPhone);
                    ");

                    // ══════════════════════════════════════════
                    //  TABLE 9: CustomerPhones (multiple phones)
                    // ══════════════════════════════════════════
                    Execute(conn, @"
                        CREATE TABLE IF NOT EXISTS CustomerPhones (
                            PhoneId         INTEGER PRIMARY KEY AUTOINCREMENT,
                            CustomerId      INTEGER NOT NULL,
                            PhoneNumber     TEXT    NOT NULL,
                            IsPrimary       INTEGER NOT NULL DEFAULT 0,
                            FOREIGN KEY (CustomerId) REFERENCES Customers(CustomerId) ON DELETE CASCADE
                        );
                    ");

                    // ══════════════════════════════════════════
                    //  TABLE 7: BILL_RETURNS (Return tracking)
                    // ══════════════════════════════════════════
                    Execute(conn, @"
                        CREATE TABLE IF NOT EXISTS BILL_RETURNS (
                            Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                            bill_id             INTEGER NOT NULL,
                            product_id          TEXT NOT NULL,
                            return_quantity     INTEGER NOT NULL,
                            original_bill_date  TEXT NOT NULL,
                            return_date         TEXT NOT NULL,
                            return_bill_id      TEXT NOT NULL,
                            FOREIGN KEY (bill_id)    REFERENCES Bill(bill_id) ON DELETE CASCADE,
                            FOREIGN KEY (product_id) REFERENCES Item(itemId)  ON DELETE RESTRICT
                        );
                    ");

                    // ── Migration: Add StockQuantity and MinStockThreshold if they don't exist ──
                    AddColumnIfNotExists(conn, "Item", "StockQuantity", "REAL NOT NULL DEFAULT 0");
                    AddColumnIfNotExists(conn, "Item", "MinStockThreshold", "REAL NOT NULL DEFAULT 10");

                    // ── Migration: Add image_path to stock table if it doesn't exist ──
                    AddColumnIfNotExists(conn, "stock", "image_path", "TEXT");

                    // ── Migration: Customer Management (Add column before index) ──
                    AddColumnIfNotExists(conn, "Bill", "CustomerId", "INTEGER");
                    AddColumnIfNotExists(conn, "Customers", "SecondaryPhone", "TEXT");

                    // ══════════════════════════════════════════
                    //  INDEXES for query optimization
                    // ══════════════════════════════════════════
                    Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Item_ItemCategory       ON Item(ItemCategory);");
                    Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Bill_BillDateTime        ON Bill(BillDateTime);");
                    Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Bill_UserId              ON Bill(UserId);");
                    Execute(conn, "CREATE INDEX IF NOT EXISTS IX_BillDesc_BillId          ON BillDescription(Bill_id);");
                    Execute(conn, "CREATE INDEX IF NOT EXISTS IX_BillDesc_ItemId          ON BillDescription(ItemId);");
                    Execute(conn, "CREATE INDEX IF NOT EXISTS IX_stock_ProductId         ON stock(product_id);");
                    Execute(conn, "CREATE INDEX IF NOT EXISTS IX_stock_BillId            ON stock(bill_id);");
                    Execute(conn, "CREATE INDEX IF NOT EXISTS IX_stock_Date              ON stock(system_date);");
                    Execute(conn, "CREATE INDEX IF NOT EXISTS IX_BillReturns_BillId      ON BILL_RETURNS(bill_id);");
                    Execute(conn, "CREATE INDEX IF NOT EXISTS IX_BillReturns_ProductId   ON BILL_RETURNS(product_id);");
                    Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Customers_Phone         ON Customers(PrimaryPhone);");
                    Execute(conn, "CREATE INDEX IF NOT EXISTS IX_CustPhones_Phone        ON CustomerPhones(PhoneNumber);");
                    Execute(conn, "CREATE INDEX IF NOT EXISTS IX_Bill_CustomerId         ON Bill(CustomerId);");

                    // ── Migration: Add Status and ReferenceBillId to Bill table ──
                    AddColumnIfNotExists(conn, "Bill", "Status", "TEXT DEFAULT 'Completed'");
                    AddColumnIfNotExists(conn, "Bill", "ReferenceBillId", "INTEGER");

                    // ── Migration: Professional Return System (Type and ParentBillId) ──
                    AddColumnIfNotExists(conn, "Bill", "Type", "TEXT DEFAULT 'Sale'");
                    AddColumnIfNotExists(conn, "Bill", "ParentBillId", "INTEGER");

                    // ── Migration: Robust Printing System (Print tracking) ──
                    AddColumnIfNotExists(conn, "Bill", "IsPrinted", "INTEGER DEFAULT 0");
                    AddColumnIfNotExists(conn, "Bill", "PrintedAt", "TEXT");
                    AddColumnIfNotExists(conn, "Bill", "PrintAttempts", "INTEGER DEFAULT 0");

                    // Sync old status to type if newly added
                    Execute(conn, "UPDATE Bill SET Type = 'Return' WHERE Status = '*** RETURN BILL ***' AND Type = 'Sale';");
                    Execute(conn, "UPDATE Bill SET ParentBillId = ReferenceBillId WHERE ReferenceBillId IS NOT NULL AND ParentBillId IS NULL;");

                    // ── Migration: Remove stale foreign keys from stock table ──
                    MigrateStockTableIfNeeded(conn);

                    SeedUsers(conn);
                    
                    // Migrate Produce and Meat & Seafood to Other
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "UPDATE Item SET ItemCategory = 'Other' WHERE ItemCategory IN ('Produce', 'Meat & Seafood');";
                        int migrated = cmd.ExecuteNonQuery();
                        if (migrated > 0)
                            AppLogger.Info($"DatabaseInitializer: Migrated {migrated} items from removed categories to 'Other'.");
                    }

                    SeedItems(conn);
                }

                AppLogger.Info("Database initialized successfully. All tables and indexes created.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Database initialization failed", ex);
                throw;
            }
        }

        // ────────────────────────────────────────────
        //  Seed default users
        // ────────────────────────────────────────────
        private static void SeedUsers(SqliteConnection conn)
        {
            // Only seed if no users exist
            using var countCmd = conn.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM User;";
            var count = Convert.ToInt64(countCmd.ExecuteScalar());
            if (count > 0) return;

            var users = new[]
            {
                ("admin",   "admin123",   "System Administrator", "Admin"),
                ("cashier", "cashier123", "Default Cashier",      "Cashier")
            };

            foreach (var (username, password, fullName, role) in users)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO User (Username, PasswordHash, FullName, Role, IsActive, CreatedAt)
                    VALUES (@username, @hash, @fullName, @role, 1, @created);
                ";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(password));
                cmd.Parameters.AddWithValue("@fullName", fullName);
                cmd.Parameters.AddWithValue("@role", role);
                cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.ExecuteNonQuery();
            }

            AppLogger.Info("Default users seeded (admin, cashier).");
        }

        // ────────────────────────────────────────────
        //  Seed sample items
        // ────────────────────────────────────────────
        private static void SeedItems(SqliteConnection conn)
        {
            // Removed early return to allow incremental seeding of new default items
            
            var items = new[]
            {
                // Dairy
                ("8961000100018", "Olper's Milk 1L",      240.0, 270.0, "Dairy"),
                ("8961000100025", "Nestle Milk Pack 1L",   230.0, 260.0, "Dairy"),
                ("8961000100032", "Adam's Cheese 200g",    450.0, 520.0, "Dairy"),
                
                // Beverages
                ("5449001000996", "Coca Cola 1.5L",        130.0, 160.0, "Beverages"),
                ("0012000001536", "Pepsi 1.5L",            125.0, 155.0, "Beverages"),
                ("8961014101111", "Nestle Pure Life 1.5L",  60.0,  80.0,  "Beverages"),
                ("8961014101112", "Red Bull 250ml",        250.0, 320.0, "Beverages"),

                // Snacks
                ("8964001510017", "Lays Classic Chips",     50.0,  70.0, "Snacks"),
                ("8964001510018", "Kurkure Chutney Chaska", 40.0,  60.0, "Snacks"),
                ("8964001510019", "Cheetos Cheese",         40.0,  60.0, "Snacks"),

                // Grocery
                ("8964001810028", "Supreme Atta 10kg",     850.0, 950.0,  "Grocery"),
                ("8961000200015", "Dalda Cooking Oil 5L", 2200.0, 2550.0, "Grocery"),
                ("8964001311014", "Tapal Danedar 950g",   1100.0, 1250.0, "Grocery"),
                ("8961000300014", "National Salt 800g",    40.0,  55.0,   "Grocery"),
                ("8961000300015", "Mehran Basmati Rice 5kg", 1800.0, 2100.0, "Grocery"),

                // Bakery
                ("8961000400011", "Dawn Bread Large",      140.0, 170.0, "Bakery"),
                ("8961000400012", "Candi Biscuits Half-Roll", 30.0, 45.0, "Bakery"),
                ("8961000400013", "Orio Biscuits Pk 12",   180.0, 220.0, "Bakery"),

                // Cleaning
                ("8961000500011", "Surf Excel 1kg",        450.0, 520.0, "Cleaning"),
                ("8961000500012", "Lemon Max Liquid 500ml", 180.0, 220.0, "Cleaning"),
                ("8961000500013", "Harpic Blue 500ml",     280.0, 340.0, "Cleaning"),

                // Personal Care
                ("8961000600011", "Lux Soap Soap 140g",    120.0, 150.0, "Personal Care"),
                ("8961000600012", "Sunsilk Shampoo 180ml", 350.0, 420.0, "Personal Care"),
                ("8961000600013", "Colgate Toothpaste 100g", 180.0, 240.0, "Personal Care"),

                // Frozen Food
                ("8961000700011", "K&Ns Nuggets 1kg",      1200.0, 1450.0, "Frozen Food"),
                ("8961000700012", "Menu Shami Kabab 12pk", 550.0, 680.0, "Frozen Food"),

                // --- NEW CATEGORIES ---
                
                // Other
                ("1000000000001", "Red Apples 1kg",        220.0, 280.0, "Other"),
                ("1000000000002", "Bananas Dozen",           140.0, 180.0, "Other"),
                ("1000000000003", "Tomatoes 1kg",            110.0, 150.0, "Other"),
                ("2000000000001", "Chicken Whole 1kg",     450.0, 550.0, "Other"),
                ("2000000000002", "Mutton Mix 1kg",        1600.0, 1950.0, "Other"),

                // Pantry & Spices
                ("3000000000001", "National Chili Powder 200g", 180.0, 230.0, "Pantry & Spices"),
                ("3000000000002", "National Turmeric 100g",      120.0, 160.0, "Pantry & Spices"),
                ("3000000000003", "Shan Ginger Garlic Paste",    320.0, 380.0, "Pantry & Spices"),

                // Household
                ("4000000000001", "Scotch-Brite Sponge 3pk",     140.0, 180.0, "Household"),
                ("4000000000002", "Large Garbage Bags 10pk",     220.0, 280.0, "Household"),

                // Baby Care
                ("5000000000001", "Pampers Baby Wipes 64ct",     450.0, 550.0, "Baby Care"),
                ("5000000000002", "Cerelac Wheat 250g",          380.0, 480.0, "Baby Care"),

                // Stationery
                ("6000000000001", "A4 Paper Ream 500 Sheets",    1400.0, 1650.0, "Stationery"),
                ("6000000000002", "Dollar Ballpoint Blue 10pk",  250.0, 320.0, "Stationery"),
            };

            int addedCount = 0;
            foreach (var (barcode, desc, cost, sale, category) in items)
            {
                using var cmd = conn.CreateCommand();
                // Using INSERT OR IGNORE to safely add only new records
                cmd.CommandText = @"
                    INSERT OR IGNORE INTO Item (itemId, Description, CostPrice, SalePrice, ItemCategory, StockQuantity)
                    VALUES (@id, @desc, @cost, @sale, @cat, 100);
                ";
                cmd.Parameters.AddWithValue("@id", barcode);
                cmd.Parameters.AddWithValue("@desc", desc);
                cmd.Parameters.AddWithValue("@cost", cost);
                cmd.Parameters.AddWithValue("@sale", sale);
                cmd.Parameters.AddWithValue("@cat", category);
                int affected = cmd.ExecuteNonQuery();
                if (affected > 0) addedCount++;
            }

            if (addedCount > 0)
                AppLogger.Info($"SeedItems: Successfully added {addedCount} new default items/categories.");
        }

        // ────────────────────────────────────────────
        //  Migration Helper
        // ────────────────────────────────────────────
        private static void AddColumnIfNotExists(SqliteConnection conn, string tableName, string columnName, string columnDefinition)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({tableName})";
            bool exists = false;
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (reader.GetString(reader.GetOrdinal("name")).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
            }

            if (!exists)
            {
                Execute(conn, $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};");
                AppLogger.Info($"DatabaseInitializer: Added column '{columnName}' to table '{tableName}'.");
            }
        }

        // ────────────────────────────────────────────
        //  Migration: Recreate stock table without FKs
        // ────────────────────────────────────────────
        private static void MigrateStockTableIfNeeded(SqliteConnection conn)
        {
            // Check if the stock table has foreign keys (old schema)
            using var fkCmd = conn.CreateCommand();
            fkCmd.CommandText = "PRAGMA foreign_key_list(stock)";
            bool hasForeignKeys = false;
            using (var reader = fkCmd.ExecuteReader())
            {
                if (reader.Read()) hasForeignKeys = true;
            }

            if (!hasForeignKeys) return;

            AppLogger.Info("DatabaseInitializer: Migrating stock table to remove stale foreign keys...");

            // Disable FK checks during migration
            Execute(conn, "PRAGMA foreign_keys = OFF;");

            using var transaction = conn.BeginTransaction();
            try
            {
                // 1. Rename old table
                Execute(conn, "ALTER TABLE stock RENAME TO stock_old;");

                // 2. Create new table without FKs
                Execute(conn, @"
                    CREATE TABLE stock (
                        Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                        product_id      TEXT    NOT NULL,
                        bill_id         TEXT    NOT NULL,
                        quantity        INTEGER NOT NULL,
                        system_date     TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
                        image_path      TEXT
                    );
                ");

                // 3. Copy data
                Execute(conn, @"
                    INSERT INTO stock (Id, product_id, bill_id, quantity, system_date, image_path)
                    SELECT Id, product_id, bill_id, quantity, system_date, image_path
                    FROM stock_old;
                ");

                // 4. Drop old table
                Execute(conn, "DROP TABLE stock_old;");

                // 5. Recreate indexes
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_stock_ProductId ON stock(product_id);");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_stock_BillId    ON stock(bill_id);");
                Execute(conn, "CREATE INDEX IF NOT EXISTS IX_stock_Date      ON stock(system_date);");

                transaction.Commit();
                AppLogger.Info("DatabaseInitializer: Stock table migrated successfully.");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                AppLogger.Error("DatabaseInitializer: Stock table migration failed", ex);
            }
            finally
            {
                // Re-enable FK checks
                Execute(conn, "PRAGMA foreign_keys = ON;");
            }
        }

        // ────────────────────────────────────────────
        //  Helper: execute a non-query SQL statement
        // ────────────────────────────────────────────
        private static void Execute(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }
}
