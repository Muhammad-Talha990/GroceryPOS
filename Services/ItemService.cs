using System;
using System.Collections.Generic;
using GroceryPOS.Data.Repositories;
using GroceryPOS.Helpers;
using GroceryPOS.Models;

namespace GroceryPOS.Services
{
    /// <summary>
    /// Business logic for item (product) management.
    /// Provides validation, CRUD delegation, and category queries.
    /// Replaces the old ProductService + Category management.
    /// </summary>
    public class ItemService
    {
        private readonly ItemRepository _repo;
        private readonly DataCacheService _cache;
        private readonly IStockService _stockService;

        public ItemService(ItemRepository repo, DataCacheService cache, IStockService stockService)
        {
            _repo = repo;
            _cache = cache;
            _stockService = stockService;
        }

        // ────────────────────────────────────────────
        //  READ (optimized with Cache)
        // ────────────────────────────────────────────

        public List<Item> GetAllItems() => _cache.GetAllItems();

        public Item? GetItemById(int id) => _cache.GetItemById(id);

        public Item? GetItemByBarcode(string barcode) => _cache.GetItemByBarcode(barcode);

        public List<Item> SearchItems(string searchTerm)
        {
            // For search, we still use Repo for complex LIKE queries, or filter cache.
            // Using repo is fine for occasional searches, but many lookups (billing) MUST use cache.
            return _repo.Search(searchTerm);
        }

        public List<Item> GetItemsByCategory(string category) => _repo.GetByCategory(category);

        public List<string> GetAllCategories() => _repo.GetAllCategories();

        public int GetTotalItemCount() => _cache.GetAllItems().Count;

        // ────────────────────────────────────────────
        //  WRITE (with validation)
        // ────────────────────────────────────────────

        /// <summary>Adds a new item after validation.</summary>
        public void AddItem(Item item)
        {
            ValidateItem(item);

            // Check for duplicate barcode in cache (only if barcode provided)
            if (!string.IsNullOrWhiteSpace(item.Barcode))
            {
                var existing = _cache.GetItemByBarcode(item.Barcode);
                if (existing != null)
                    throw new InvalidOperationException($"An item with barcode '{item.Barcode}' already exists ({existing.Description}).");
            }

            _repo.Add(item);

            // Sync Cache
            _cache.UpdateItemInCache(item);
            _stockService.NotifyChanged();
        }

        /// <summary>Updates an existing item after validation.</summary>
        public void UpdateItem(Item item, string? originalBarcode = null)
        {
            ValidateItem(item);
            
            // if we are changing the barcode, check for collisions
            if (!string.IsNullOrEmpty(item.Barcode) && !string.IsNullOrEmpty(originalBarcode) && originalBarcode != item.Barcode)
            {
               var existing = _cache.GetItemByBarcode(item.Barcode);
               if (existing != null && existing.Id != item.Id)
                   throw new InvalidOperationException($"An item with barcode '{item.Barcode}' already exists.");
            }

            _repo.Update(item, originalBarcode);

            // Sync Cache
            _cache.UpdateItemInCache(item);
            _stockService.NotifyChanged();
        }

        /// <summary>Deletes an item by barcode. Throws on FK constraint (linked to bills).</summary>
        public void DeleteItem(int id)
        {
            _repo.Delete(id);

            // Sync Cache
            _cache.RemoveItemFromCache(id);
            _stockService.NotifyChanged();
        }

        // ────────────────────────────────────────────
        //  Validation
        // ────────────────────────────────────────────

        private void ValidateItem(Item item)
        {
            if (string.IsNullOrWhiteSpace(item.Description))
                throw new ArgumentException("Item description is required.");

            if (item.CostPrice < 0)
                throw new ArgumentException("Cost price cannot be negative.");

            if (item.SalePrice < 0)
                throw new ArgumentException("Sale price cannot be negative.");
        }
    }
}
