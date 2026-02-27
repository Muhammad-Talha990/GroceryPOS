using System;
using System.Collections.Generic;
using System.Linq;
using GroceryPOS.Data.Repositories;
using GroceryPOS.Models;

namespace GroceryPOS.Services
{
    public class StockService : IStockService
    {
        private readonly ItemRepository _itemRepo;
        private readonly DataCacheService _cache;

        public event Action? StockChanged;

        public StockService(ItemRepository itemRepo, DataCacheService cache)
        {
            _itemRepo = itemRepo;
            _cache = cache;
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
