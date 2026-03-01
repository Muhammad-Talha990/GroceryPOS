using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using GroceryPOS.Helpers;
using GroceryPOS.Models;

namespace GroceryPOS.Data.Repositories
{
    /// <summary>
    /// Data access for the Item table.
    /// Provides CRUD operations using raw SQL with parameterized queries.
    /// </summary>
    public class ItemRepository
    {
        // ────────────────────────────────────────────
        //  READ operations
        // ────────────────────────────────────────────

        /// <summary>Returns all items ordered by description.</summary>
        public List<Item> GetAll()
        {
            var items = new List<Item>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Item ORDER BY Description;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                items.Add(MapItem(reader));

            return items;
        }

        /// <summary>Gets a single item by barcode (primary key).</summary>
        public Item? GetByBarcode(string barcode)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Item WHERE itemId = @id;";
            cmd.Parameters.AddWithValue("@id", barcode);

            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapItem(reader) : null;
        }

        /// <summary>Searches items by description, barcode, or category (case-insensitive).</summary>
        public List<Item> Search(string searchTerm)
        {
            var items = new List<Item>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT * FROM Item 
                WHERE Description  LIKE @term 
                   OR itemId       LIKE @term 
                   OR ItemCategory LIKE @term
                ORDER BY Description;
            ";
            cmd.Parameters.AddWithValue("@term", $"%{searchTerm}%");

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                items.Add(MapItem(reader));

            return items;
        }

        /// <summary>Gets all items in a specific category.</summary>
        public List<Item> GetByCategory(string category)
        {
            var items = new List<Item>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Item WHERE ItemCategory = @cat ORDER BY Description;";
            cmd.Parameters.AddWithValue("@cat", category);

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
            cmd.CommandText = "SELECT DISTINCT ItemCategory FROM Item WHERE ItemCategory IS NOT NULL ORDER BY ItemCategory;";

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
            cmd.CommandText = "SELECT COUNT(*) FROM Item;";
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
                INSERT INTO Item (itemId, Description, CostPrice, SalePrice, ItemCategory, StockQuantity, MinStockThreshold)
                VALUES (@id, @desc, @cost, @sale, @cat, @stock, @threshold);
            ";
            cmd.Parameters.AddWithValue("@id", item.ItemId);
            cmd.Parameters.AddWithValue("@desc", item.Description);
            cmd.Parameters.AddWithValue("@cost", item.CostPrice);
            cmd.Parameters.AddWithValue("@sale", item.SalePrice);
            cmd.Parameters.AddWithValue("@cat", (object?)item.ItemCategory ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@stock", item.StockQuantity);
            cmd.Parameters.AddWithValue("@threshold", item.MinStockThreshold);
            cmd.ExecuteNonQuery();

            AppLogger.Info($"Item added: '{item.Description}' (Barcode: {item.ItemId})");
        }

        /// <summary>Updates an existing item by barcode.</summary>
        public void Update(Item item)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Item SET 
                    Description   = @desc,
                    CostPrice     = @cost,
                    SalePrice     = @sale,
                    ItemCategory  = @cat,
                    MinStockThreshold = @threshold
                WHERE itemId = @id;
            ";
            cmd.Parameters.AddWithValue("@id", item.ItemId);
            cmd.Parameters.AddWithValue("@desc", item.Description);
            cmd.Parameters.AddWithValue("@cost", item.CostPrice);
            cmd.Parameters.AddWithValue("@sale", item.SalePrice);
            cmd.Parameters.AddWithValue("@cat", (object?)item.ItemCategory ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@threshold", item.MinStockThreshold);
            cmd.ExecuteNonQuery();

            AppLogger.Info($"Item updated (info only): '{item.Description}' (Barcode: {item.ItemId})");
        }

        /// <summary>Permanently deletes an item by barcode.</summary>
        public void Delete(string barcode)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Item WHERE itemId = @id;";
            cmd.Parameters.AddWithValue("@id", barcode);
            cmd.ExecuteNonQuery();

            AppLogger.Info($"Item deleted: Barcode {barcode}");
        }

        /// <summary>
        /// Updates only the stock quantity for an item (thread-safe/atomic in SQLite).
        /// </summary>
        public void UpdateStock(string barcode, double quantityChange)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Item SET StockQuantity = StockQuantity + @change WHERE itemId = @id;";
            cmd.Parameters.AddWithValue("@change", quantityChange);
            cmd.Parameters.AddWithValue("@id", barcode);
            cmd.ExecuteNonQuery();

            AppLogger.Info($"Stock updated for {barcode}: {quantityChange:N2}");
        }


        // ────────────────────────────────────────────
        //  Mapper
        // ────────────────────────────────────────────

        private static Item MapItem(SqliteDataReader reader)
        {
            return new Item
            {
                ItemId        = reader.GetString(reader.GetOrdinal("itemId")),
                Description   = reader.GetString(reader.GetOrdinal("Description")),
                CostPrice     = reader.GetDouble(reader.GetOrdinal("CostPrice")),
                SalePrice     = reader.GetDouble(reader.GetOrdinal("SalePrice")),
                ItemCategory  = reader.IsDBNull(reader.GetOrdinal("ItemCategory")) ? null : reader.GetString(reader.GetOrdinal("ItemCategory")),
                StockQuantity = reader.GetDouble(reader.GetOrdinal("StockQuantity")),
                MinStockThreshold = reader.GetDouble(reader.GetOrdinal("MinStockThreshold"))
            };
        }
    }
}
