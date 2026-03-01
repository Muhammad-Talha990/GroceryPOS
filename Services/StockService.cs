using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GroceryPOS.Data.Repositories;
using GroceryPOS.Models;
using GroceryPOS.Helpers;

namespace GroceryPOS.Services
{
    public class StockService : IStockService
    {
        private readonly ItemRepository _itemRepo;
        private readonly DataCacheService _cache;
        private readonly IImageStorageService _imageService;
        private readonly UniqueIdGenerator _idGenerator;

        public event Action? StockChanged;

        public StockService(ItemRepository itemRepo, DataCacheService cache, IImageStorageService imageService, UniqueIdGenerator idGenerator)
        {
            _itemRepo = itemRepo;
            _cache = cache;
            _imageService = imageService;
            _idGenerator = idGenerator;
        }

        public void NotifyChanged() => StockChanged?.Invoke();

        public void DeductStock(string barcode, double quantity)
        {
            var item = _itemRepo.GetByBarcode(barcode);
            if (item == null)
                throw new InvalidOperationException($"Product with barcode {barcode} not found.");

            if (item.StockQuantity < quantity)
                throw new InvalidOperationException($"Insufficient stock for '{item.Description}'. Available: {item.StockQuantity}, Requested: {quantity}");

            _itemRepo.UpdateStock(barcode, -quantity);
            _cache.UpdateStockInCache(barcode, -quantity);
            StockChanged?.Invoke();
        }

        public void AddStock(string barcode, double quantity)
        {
            if (quantity <= 0)
                throw new ArgumentException("Quantity to add must be positive.");

            var item = _itemRepo.GetByBarcode(barcode);
            if (item == null)
                throw new InvalidOperationException($"Product with barcode {barcode} not found.");

            _itemRepo.UpdateStock(barcode, quantity);
            _cache.UpdateStockInCache(barcode, quantity);
            StockChanged?.Invoke();
        }

        public async Task<bool> RegisterSupplyAsync(Stock entry, string? tempImagePath)
        {
            if (entry.Quantity <= 0)
                throw new ArgumentException("Supply quantity must be positive.");

            // 1. Generate Internal ID
            entry.BillId = _idGenerator.GenerateSupplierBillId();

            // 2. Process Image (Rename and save)
            string? permanentImagePath = null;
            if (!string.IsNullOrEmpty(tempImagePath))
            {
                permanentImagePath = await _imageService.SaveBillImageAsync(tempImagePath, entry.BillId);
                entry.ImagePath = permanentImagePath;
            }

            // 3. Transactional Operations
            using var conn = Data.DatabaseHelper.GetConnection();
            using var transaction = conn.BeginTransaction();
            try
            {
                // 3a. Save Entry to stock table
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO stock (product_id, bill_id, quantity, system_date, image_path)
                        VALUES (@pid, @bid, @qty, @dt, @img);
                    ";
                    cmd.Parameters.AddWithValue("@pid", entry.ProductId);
                    cmd.Parameters.AddWithValue("@bid", entry.BillId);
                    cmd.Parameters.AddWithValue("@qty", entry.Quantity);
                    cmd.Parameters.AddWithValue("@dt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@img", (object?)entry.ImagePath ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }

                // 3b. Update Main Product Stock
                using (var updateCmd = conn.CreateCommand())
                {
                    updateCmd.Transaction = transaction;
                    updateCmd.CommandText = "UPDATE Item SET StockQuantity = StockQuantity + @qty WHERE itemId = @pid;";
                    updateCmd.Parameters.AddWithValue("@qty", entry.Quantity);
                    updateCmd.Parameters.AddWithValue("@pid", entry.ProductId);
                    updateCmd.ExecuteNonQuery();
                }

                transaction.Commit();

                // 4. Update Cache & UI
                _cache.UpdateStockInCache(entry.ProductId, entry.Quantity);
                StockChanged?.Invoke();

                AppLogger.Info($"Supply registered: {entry.Quantity} units for {entry.ProductId}. Bill ID: {entry.BillId}");
                return true;
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                // Cleanup image if saved but transaction failed
                if (!string.IsNullOrEmpty(permanentImagePath))
                    _imageService.DeleteBillImage(entry.BillId);

                AppLogger.Error($"Failed to register supply for {entry.ProductId}", ex);
                throw;
            }
        }

        public async Task<List<Stock>> GetSupplyHistoryAsync(string productId)
        {
            var history = new List<Stock>();
            using var conn = Data.DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT s.*, i.Description AS ProductName
                FROM stock s 
                LEFT JOIN Item i ON s.product_id = i.itemId
                WHERE s.product_id = @pid 
                ORDER BY s.system_date DESC;";
            cmd.Parameters.AddWithValue("@pid", productId);

            using var reader = await cmd.ExecuteReaderAsync();
            while (reader.Read())
            {
                history.Add(new Stock
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    ProductId = reader.GetString(reader.GetOrdinal("product_id")),
                    BillId = reader.GetString(reader.GetOrdinal("bill_id")),
                    Quantity = reader.GetInt32(reader.GetOrdinal("quantity")),
                    SystemDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("system_date"))),
                    ImagePath = reader.IsDBNull(reader.GetOrdinal("image_path")) ? null : reader.GetString(reader.GetOrdinal("image_path")),
                    ProductName = reader.IsDBNull(reader.GetOrdinal("ProductName")) ? "" : reader.GetString(reader.GetOrdinal("ProductName"))
                });
            }
            return history;
        }

        public async Task<List<Stock>> GetAllRecentSuppliesAsync(int limit = 50)
        {
            var history = new List<Stock>();
            using var conn = Data.DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT s.*, i.Description AS ProductName
                FROM stock s 
                LEFT JOIN Item i ON s.product_id = i.itemId
                ORDER BY s.system_date DESC 
                LIMIT @limit;";
            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = await cmd.ExecuteReaderAsync();
            while (reader.Read())
            {
                history.Add(new Stock
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    ProductId = reader.GetString(reader.GetOrdinal("product_id")),
                    BillId = reader.GetString(reader.GetOrdinal("bill_id")),
                    Quantity = reader.GetInt32(reader.GetOrdinal("quantity")),
                    SystemDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("system_date"))),
                    ImagePath = reader.IsDBNull(reader.GetOrdinal("image_path")) ? null : reader.GetString(reader.GetOrdinal("image_path")),
                    ProductName = reader.IsDBNull(reader.GetOrdinal("ProductName")) ? "" : reader.GetString(reader.GetOrdinal("ProductName"))
                });
            }
            return history;
        }

        public async Task<bool> DeleteSupplyAsync(int stockId)
        {
            using var conn = Data.DatabaseHelper.GetConnection();
            
            // 1. Fetch entry details first for cleanup
            Stock? entry = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM stock WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", stockId);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (reader.Read())
                    {
                        entry = new Stock
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("Id")),
                            ProductId = reader.GetString(reader.GetOrdinal("product_id")),
                            BillId = reader.GetString(reader.GetOrdinal("bill_id")),
                            Quantity = reader.GetInt32(reader.GetOrdinal("quantity")),
                            ImagePath = reader.IsDBNull(reader.GetOrdinal("image_path")) ? null : reader.GetString(reader.GetOrdinal("image_path"))
                        };
                    }
                }
            }

            if (entry == null) return false;

            using var transaction = conn.BeginTransaction();
            try
            {
                // 2. Delete from stock table
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "DELETE FROM stock WHERE Id = @id;";
                    cmd.Parameters.AddWithValue("@id", stockId);
                    cmd.ExecuteNonQuery();
                }

                // 3. Revert Main Product Stock
                using (var updateCmd = conn.CreateCommand())
                {
                    updateCmd.Transaction = transaction;
                    updateCmd.CommandText = "UPDATE Item SET StockQuantity = StockQuantity - @qty WHERE itemId = @pid;";
                    updateCmd.Parameters.AddWithValue("@qty", entry.Quantity);
                    updateCmd.Parameters.AddWithValue("@pid", entry.ProductId);
                    updateCmd.ExecuteNonQuery();
                }

                transaction.Commit();

                // 4. Cleanup physical image
                if (!string.IsNullOrEmpty(entry.BillId))
                {
                    _imageService.DeleteBillImage(entry.BillId);
                }

                // 5. Update Cache & UI
                _cache.UpdateStockInCache(entry.ProductId, -entry.Quantity);
                StockChanged?.Invoke();

                AppLogger.Info($"Supply deleted: {entry.Quantity} units for {entry.ProductId}. Bill ID: {entry.BillId}");
                return true;
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                AppLogger.Error($"Failed to delete supply {stockId}", ex);
                throw;
            }
        }

        public bool IsStockAvailable(string barcode, double requiredQuantity, out double availableQuantity)
        {
            var item = _itemRepo.GetByBarcode(barcode);
            if (item == null)
            {
                availableQuantity = 0;
                return false;
            }

            availableQuantity = item.StockQuantity;
            return item.StockQuantity >= requiredQuantity;
        }

        public List<Item> GetLowStockItems()
        {
            return _itemRepo.GetAll()
                .Where(i => i.StockQuantity <= i.MinStockThreshold)
                .OrderBy(i => i.StockQuantity)
                .ToList();
        }

        public int GetLowStockCount()
        {
            return _itemRepo.GetAll()
                .Count(i => i.StockQuantity <= i.MinStockThreshold);
        }
    }
}
