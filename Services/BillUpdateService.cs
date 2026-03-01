using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GroceryPOS.Data;
using GroceryPOS.Data.Repositories;
using GroceryPOS.Exceptions;
using GroceryPOS.Models;

namespace GroceryPOS.Services
{
    public class BillUpdateService : IBillUpdateService
    {
        private readonly BillRepository _billRepo;
        private readonly BillService _billService;
        private readonly DataCacheService _cache;
        private readonly IStockService _stockService;

        public BillUpdateService(
            BillRepository billRepo, 
            BillService billService, 
            DataCacheService cache, 
            IStockService stockService)
        {
            _billRepo = billRepo;
            _billService = billService;
            _cache = cache;
            _stockService = stockService;
        }

        public async Task<Bill> UpdateBill(int originalBillId, List<BillDescription> updatedItems, double newDiscount, double newTax, double newCashReceived, int? userId)
        {
            if (updatedItems == null || !updatedItems.Any())
                throw new BusinessException("Updated bill must contain at least one item.");

            return await Task.Run(() =>
            {
                var originalBill = _billRepo.GetById(originalBillId);
                if (originalBill == null)
                    throw new BusinessException($"Original bill #{originalBillId} not found.");

                if (originalBill.Status == "Cancelled")
                    throw new BusinessException($"Bill #{originalBillId} has been cancelled and cannot be updated.");

                // Recalculate TotalPrice for each item
                foreach (var item in updatedItems)
                    item.TotalPrice = item.Quantity * item.UnitPrice;

                // Calculate new totals
                double subTotal = updatedItems.Sum(i => i.TotalPrice);
                double grandTotal = Math.Round(subTotal - newDiscount + newTax, 2);
                double changeGiven = Math.Round(newCashReceived - grandTotal, 2);

                if (newCashReceived < grandTotal)
                    throw new BusinessException($"Insufficient cash. Required: {grandTotal:N2}, Received: {newCashReceived:N2}");

                // Validate stock availability for increased quantities
                foreach (var newItem in updatedItems)
                {
                    var oldItem = originalBill.Items.FirstOrDefault(o => o.ItemId == newItem.ItemId);
                    int oldQty = oldItem?.Quantity ?? 0;
                    int extraNeeded = newItem.Quantity - oldQty;
                    if (extraNeeded > 0)
                    {
                        if (!_stockService.IsStockAvailable(newItem.ItemId, extraNeeded, out double available))
                            throw new BusinessException($"Insufficient stock for {newItem.ItemDescription}. Available: {available}, Extra needed: {extraNeeded}");
                    }
                }

                using var conn = DatabaseHelper.GetConnection();
                using var txn = conn.BeginTransaction();

                try
                {
                    // 1. Reverse old stock quantities (add back to inventory)
                    foreach (var oldItem in originalBill.Items)
                    {
                        using var reverseCmd = conn.CreateCommand();
                        reverseCmd.Transaction = txn;
                        reverseCmd.CommandText = "UPDATE Item SET StockQuantity = StockQuantity + @qty WHERE itemId = @id;";
                        reverseCmd.Parameters.AddWithValue("@qty", oldItem.Quantity);
                        reverseCmd.Parameters.AddWithValue("@id", oldItem.ItemId);
                        reverseCmd.ExecuteNonQuery();

                        _cache.UpdateStockInCache(oldItem.ItemId, oldItem.Quantity);
                    }

                    // 2. Update the bill in-place (same bill_id)
                    originalBill.SubTotal = subTotal;
                    originalBill.DiscountAmount = newDiscount;
                    originalBill.TaxAmount = newTax;
                    originalBill.GrandTotal = grandTotal;
                    originalBill.CashReceived = newCashReceived;
                    originalBill.ChangeGiven = changeGiven;
                    originalBill.BillDateTime = DateTime.Now;

                    _billRepo.UpdateBillInPlace(originalBill, updatedItems, conn, txn);

                    // 3. Deduct new stock quantities
                    foreach (var newItem in updatedItems)
                    {
                        using var deductCmd = conn.CreateCommand();
                        deductCmd.Transaction = txn;
                        deductCmd.CommandText = "UPDATE Item SET StockQuantity = StockQuantity - @qty WHERE itemId = @id;";
                        deductCmd.Parameters.AddWithValue("@qty", newItem.Quantity);
                        deductCmd.Parameters.AddWithValue("@id", newItem.ItemId);
                        deductCmd.ExecuteNonQuery();

                        _cache.UpdateStockInCache(newItem.ItemId, -newItem.Quantity);
                    }

                    txn.Commit();
                    _stockService.NotifyChanged();

                    originalBill.Items = updatedItems;
                    return originalBill;
                }
                catch (Exception ex)
                {
                    txn.Rollback();
                    if (ex is BusinessException) throw;
                    throw new BusinessException("An error occurred while updating the bill.", ex);
                }
            });
        }
    }
}
