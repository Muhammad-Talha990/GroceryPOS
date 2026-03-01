using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GroceryPOS.Data;
using GroceryPOS.Data.Repositories;
using GroceryPOS.Exceptions;
using GroceryPOS.Models;
using Microsoft.Data.Sqlite;
using GroceryPOS.Helpers;

namespace GroceryPOS.Services
{
    public class ReturnService : IReturnService
    {
        private readonly BillRepository _billRepo;
        private readonly BillReturnRepository _returnRepo;
        private readonly DataCacheService _cache;
        private readonly IStockService _stockService;

        public ReturnService(
            BillRepository billRepo, 
            BillReturnRepository returnRepo, 
            DataCacheService cache, 
            IStockService stockService)
        {
            _billRepo = billRepo;
            _returnRepo = returnRepo;
            _cache = cache;
            _stockService = stockService;
        }

        public async Task<Bill?> GetBillForReturn(int billId)
        {
            return await Task.Run(() => _billRepo.GetById(billId));
        }

        public int GetTotalReturnedQuantity(int originalBillId, string productId)
        {
            return _returnRepo.GetTotalReturnedQuantity(originalBillId, productId);
        }

        public async Task<bool> ValidateReturnQuantity(int originalBillId, string productId, int requestedQuantity)
        {
            return await Task.Run(() =>
            {
                var originalBill = _billRepo.GetById(originalBillId);
                if (originalBill == null) return false;

                var origItem = originalBill.Items.FirstOrDefault(i => i.ItemId == productId);
                if (origItem == null) return false;

                int alreadyReturned = GetTotalReturnedQuantity(originalBillId, productId);
                return (alreadyReturned + requestedQuantity) <= origItem.Quantity;
            });
        }

        public async Task<(Bill Original, List<Bill> Returns)> GetBillWithReturnHistory(int billId)
        {
            return await Task.Run(() =>
            {
                var original = _billRepo.GetById(billId);
                if (original == null) throw new BusinessException($"Bill #{billId} not found.");

                var returns = _billRepo.GetReturnsByParentId(billId);
                return (original, returns);
            });
        }

        public async Task<Bill> CreateReturn(int originalBillId, int? userId, List<BillDescription> items)
        {
             if (items == null || !items.Any())
                throw new BusinessException("No items selected for return.");

            return await Task.Run(() =>
            {
                var originalBill = _billRepo.GetById(originalBillId);
                if (originalBill == null)
                    throw new BusinessException($"Original bill #{originalBillId} not found.");

                using var conn = DatabaseHelper.GetConnection();
                using var txn = conn.BeginTransaction();

                try
                {
                    var returnBillItems = new List<BillDescription>();
                    double subTotal = 0;

                    foreach (var returnItem in items)
                    {
                        var origItem = originalBill.Items.FirstOrDefault(i => i.ItemId == returnItem.ItemId);
                        if (origItem == null)
                            throw new BusinessException($"Item {returnItem.ItemId} was not part of original bill #{originalBillId}.");

                        int alreadyReturned = GetTotalReturnedQuantity(originalBillId, returnItem.ItemId);
                        int remaining = (int)origItem.Quantity - alreadyReturned;
                        
                        if (returnItem.Quantity > remaining)
                            throw new BusinessException($"Return quantity exceeds original sold quantity for {origItem.ItemDescription}. (Requested: {returnItem.Quantity}, Available: {remaining})");

                        if (returnItem.Quantity <= 0) continue;

                        // Prepare return bill line item (negative values for accounting/stock)
                        var returnBillItem = new BillDescription
                        {
                            ItemId = returnItem.ItemId,
                            Quantity = -returnItem.Quantity,
                            UnitPrice = origItem.UnitPrice,
                            TotalPrice = -(returnItem.Quantity * origItem.UnitPrice),
                            ItemDescription = origItem.ItemDescription
                        };
                        returnBillItems.Add(returnBillItem);
                        subTotal += returnBillItem.TotalPrice;
                    }

                    if (!returnBillItems.Any())
                        throw new BusinessException("No valid items to return.");

                    var returnBill = new Bill
                    {
                        BillDateTime = DateTime.Now,
                        SubTotal = subTotal,
                        DiscountAmount = 0,
                        TaxAmount = 0,
                        GrandTotal = subTotal,
                        CashReceived = 0,
                        ChangeGiven = 0,
                        UserId = userId,
                        Type = "Return",
                        ParentBillId = originalBillId,
                        Status = "Return processed"
                    };

                    var savedReturnBill = _billRepo.SaveBillInternal(returnBill, returnBillItems, conn, txn);
                    
                    // Legacy record keeping for extra audit safety
                    foreach (var returnItem in items.Where(i => i.Quantity > 0))
                    {
                        var billReturn = new BillReturn
                        {
                            BillId = originalBillId,
                            ProductId = returnItem.ItemId,
                            ReturnQuantity = (int)returnItem.Quantity,
                            OriginalBillDate = originalBill.BillDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                            ReturnDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            ReturnBillId = savedReturnBill.InvoiceNumber
                        };
                        _returnRepo.Insert(billReturn, conn, txn);
                    }

                    txn.Commit();

                    // Update Cache
                    foreach (var item in returnBillItems)
                    {
                        _cache.UpdateStockInCache(item.ItemId, -item.Quantity);
                    }
                    _stockService.NotifyChanged();

                    return savedReturnBill;
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Professional return creation failed", ex);
                    try { if (txn.Connection != null) txn.Rollback(); } catch { }
                    if (ex is BusinessException) throw;
                    throw new BusinessException($"Return failed: {ex.Message}", ex);
                }
            });
        }

        public int GetRemainingReturnableQuantity(int billId, string productId, int originalQuantity)
        {
            int alreadyReturned = GetTotalReturnedQuantity(billId, productId);
            return originalQuantity - alreadyReturned;
        }

        public async Task<List<BillReturn>> GetReturnHistory(int billId)
        {
            return await Task.Run(() => _returnRepo.GetByOriginalBillId(billId));
        }

        public async Task<Bill> ProcessReturn(int originalBillId, int? userId, List<BillDescription> itemsToReturn)
        {
            return await CreateReturn(originalBillId, userId, itemsToReturn);
        }
    }
}
