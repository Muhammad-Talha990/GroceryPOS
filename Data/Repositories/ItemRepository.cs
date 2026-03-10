using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using GroceryPOS.Helpers;
using GroceryPOS.Models;

namespace GroceryPOS.Data.Repositories
{
    /// <summary>
    /// Data access for the Items table (Normalized 3NF).
    /// Provides CRUD operations using raw SQL with parameterized queries.
    /// </summary>
    public class ItemRepository
    {
        private const string BaseSelectSql = @"
            SELECT i.*, c.Name as CategoryName, 
                   COALESCE((SELECT SUM(QuantityChange) FROM InventoryLogs WHERE ItemId = i.ItemId), 0) as StockQuantity
            FROM Items i
            LEFT JOIN Categories c ON i.CategoryId = c.CategoryId";

        // ────────────────────────────────────────────
        //  READ operations
        // ────────────────────────────────────────────

        /// <summary>Returns all items ordered by description.</summary>
        public List<Item> GetAll()
        {
            var items = new List<Item>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"{BaseSelectSql} ORDER BY i.Description;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                items.Add(MapItem(reader));

            return items;
        }

        /// <summary>Gets a single item by barcode.</summary>
        public Item? GetByBarcode(string barcode)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"{BaseSelectSql} WHERE i.Barcode = @barcode;";
            cmd.Parameters.AddWithValue("@barcode", barcode);

            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapItem(reader) : null;
        }

        /// <summary>Gets a single item by its internal primary key ID.</summary>
        public Item? GetById(int id)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"{BaseSelectSql} WHERE i.ItemId = @id;";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapItem(reader) : null;
        }

        /// <summary>Searches items by description, barcode, or category (case-insensitive).</summary>
        public List<Item> Search(string searchTerm)
        {
            var items = new List<Item>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                {BaseSelectSql}
                WHERE i.Description LIKE @term 
                   OR i.Barcode     LIKE @term 
                   OR c.Name        LIKE @term
                ORDER BY i.Description;
            ";
            cmd.Parameters.AddWithValue("@term", $"%{searchTerm}%");

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                items.Add(MapItem(reader));

            return items;
        }

        /// <summary>Gets all items in a specific category name.</summary>
        public List<Item> GetByCategory(string categoryName)
        {
            var items = new List<Item>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"{BaseSelectSql} WHERE c.Name = @cat ORDER BY i.Description;";
            cmd.Parameters.AddWithValue("@cat", categoryName);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                items.Add(MapItem(reader));

            return items;
        }

        /// <summary>Returns all distinct category names.</summary>
        public List<string> GetAllCategories()
        {
            var categories = new List<string>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Name FROM Categories ORDER BY Name;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                categories.Add(reader.GetString(0));

            return categories;
        }

        /// <summary>Returns total count of items.</summary>
        public int GetCount()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Items;";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        // ────────────────────────────────────────────
        //  WRITE operations
        // ────────────────────────────────────────────

        /// <summary>Inserts a new item. Throws if barcode already exists.</summary>
        public void Add(Item item)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Items (Barcode, Description, CostPrice, SalePrice, CategoryId, MinStockThreshold)
                VALUES (@barcode, @desc, @cost, @sale, 
                       (SELECT CategoryId FROM Categories WHERE Name = @catName), 
                       @threshold);
            ";
            cmd.Parameters.AddWithValue("@barcode", string.IsNullOrWhiteSpace(item.Barcode) ? DBNull.Value : item.Barcode);
            cmd.Parameters.AddWithValue("@desc", item.Description);
            cmd.Parameters.AddWithValue("@cost", item.CostPrice);
            cmd.Parameters.AddWithValue("@sale", item.SalePrice);
            cmd.Parameters.AddWithValue("@catName", (object?)item.CategoryName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@threshold", item.MinStockThreshold);
            cmd.ExecuteNonQuery();

            // Retrieve the auto-generated ItemId
            using var idCmd = conn.CreateCommand();
            idCmd.CommandText = "SELECT last_insert_rowid();";
            item.Id = Convert.ToInt32(idCmd.ExecuteScalar()!);

            // Record initial stock as a Purchase log entry
            if (item.StockQuantity > 0)
            {
                RecordStockChange(item.Id, item.StockQuantity, "Purchase");
            }

            AppLogger.Info($"Item added: '{item.Description}' (Barcode: {item.Barcode}, Id: {item.Id})");
        }

        /// <summary>Updates an existing item.</summary>
        public void Update(Item item)
        {
            Update(item, item.Barcode);
        }

        /// <summary>Updates an item, specifically handling barcode changes if originalBarcode is provided.</summary>
        public void Update(Item item, string? originalBarcode)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Items SET 
                    Barcode       = @barcode,
                    Description   = @desc,
                    CostPrice     = @cost,
                    SalePrice     = @sale,
                    CategoryId    = (SELECT CategoryId FROM Categories WHERE Name = @catName),
                    MinStockThreshold = @threshold
                WHERE ItemId = @id;
            ";
            cmd.Parameters.AddWithValue("@id", item.Id);
            cmd.Parameters.AddWithValue("@barcode", string.IsNullOrWhiteSpace(item.Barcode) ? DBNull.Value : item.Barcode);
            cmd.Parameters.AddWithValue("@desc", item.Description);
            cmd.Parameters.AddWithValue("@cost", item.CostPrice);
            cmd.Parameters.AddWithValue("@sale", item.SalePrice);
            cmd.Parameters.AddWithValue("@catName", (object?)item.CategoryName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@threshold", item.MinStockThreshold);
            cmd.ExecuteNonQuery();

            AppLogger.Info($"Item updated: '{item.Description}' (Barcode: {item.Barcode})");
        }

        /// <summary>Permanently deletes an item by internal ID.</summary>
        public void Delete(int id)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Items WHERE ItemId = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            AppLogger.Info($"Item deleted: ID {id}");
        }

        /// <summary>Permanently deletes an item by barcode.</summary>
        public void Delete(string barcode)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Items WHERE Barcode = @barcode;";
            cmd.Parameters.AddWithValue("@barcode", barcode);
            cmd.ExecuteNonQuery();
            AppLogger.Info($"Item deleted: Barcode {barcode}");
        }

        /// <summary>Updates stock by recording a log entry (compatibility shim).</summary>
        public void UpdateStock(string barcode, double change)
        {
            var item = GetByBarcode(barcode);
            if (item != null)
            {
                RecordStockChange(item.Id, change, change > 0 ? "AdjustmentIn" : "AdjustmentOut", "Manual Stock Update");
            }
        }

        /// <summary>
        /// Records a stock change in the InventoryLogs.
        /// </summary>
        public void RecordStockChange(int itemId, double change, string type, string? note = null)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO InventoryLogs (ItemId, QuantityChange, ChangeType) 
                VALUES (@id, @change, @type);";
            cmd.Parameters.AddWithValue("@id", itemId);
            cmd.Parameters.AddWithValue("@change", change);
            cmd.Parameters.AddWithValue("@type", type);
            cmd.ExecuteNonQuery();

            AppLogger.Info($"Stock LOGGED for Item ID {itemId}: {change:N2} ({type})");
        }


        // ────────────────────────────────────────────
        //  Mapper
        // ────────────────────────────────────────────

        private static Item MapItem(SqliteDataReader reader)
        {
            var barcodeOrd = reader.GetOrdinal("Barcode");
            return new Item
            {
                Id                = reader.GetInt32(reader.GetOrdinal("ItemId")),
                Barcode           = reader.IsDBNull(barcodeOrd) ? null : reader.GetString(barcodeOrd),
                Description       = reader.GetString(reader.GetOrdinal("Description")),
                CostPrice         = reader.GetDouble(reader.GetOrdinal("CostPrice")),
                SalePrice         = reader.GetDouble(reader.GetOrdinal("SalePrice")),
                CategoryId        = reader.IsDBNull(reader.GetOrdinal("CategoryId")) ? (int?)null : reader.GetInt32(reader.GetOrdinal("CategoryId")),
                CategoryName      = reader.IsDBNull(reader.GetOrdinal("CategoryName")) ? null : reader.GetString(reader.GetOrdinal("CategoryName")),
                StockQuantity     = reader.GetDouble(reader.GetOrdinal("StockQuantity")),
                MinStockThreshold = reader.GetDouble(reader.GetOrdinal("MinStockThreshold"))
            };
        }
    }
}
