using System.Collections.Generic;
using GroceryPOS.Models;

namespace GroceryPOS.Services
{
    public interface IStockService
    {
        event System.Action? StockChanged;
        void NotifyChanged();
        void DeductStock(string barcode, double quantity);
        void AddStock(string barcode, double quantity);
        bool IsStockAvailable(string barcode, double requiredQuantity, out double availableQuantity);
        List<Item> GetLowStockItems();
        int GetLowStockCount();
    }
}
