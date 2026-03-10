using System.Collections.Generic;
using System.Threading.Tasks;
using GroceryPOS.Models;

namespace GroceryPOS.Services
{
    public interface IStockService
    {
        event System.Action? StockChanged;
        void NotifyChanged();
        void DeductStock(string barcode, double quantity);
        void AddStock(string barcode, double quantity);
        
        // --- New Supply Management ---
        Task<bool> RegisterSupplyAsync(Stock entry, string? tempImagePath);
        Task<List<Stock>> GetSupplyHistoryAsync(string productId);
        Task<List<Stock>> GetAllRecentSuppliesAsync(int limit = 50);
        Task<bool> UpdateSupplyAsync(Stock entry, string? tempImagePath);
        Task<bool> DeleteSupplyAsync(int stockId);
        
        bool IsStockAvailable(string barcode, double requiredQuantity, out double availableQuantity);
        bool IsStockAvailable(int itemId, double requiredQuantity, out double availableQuantity);
        List<Item> GetLowStockItems();
        int GetLowStockCount();
    }
}
