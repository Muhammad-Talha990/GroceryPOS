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
        private readonly CreditPaymentRepository _creditRepo;

        public ReturnService(
            BillRepository billRepo, 
            BillReturnRepository returnRepo, 
            DataCacheService cache, 
            IStockService stockService,
            CreditPaymentRepository creditRepo)
        {
            _billRepo = billRepo;
            _returnRepo = returnRepo;
            _cache = cache;
            _stockService = stockService;
            _creditRepo = creditRepo;
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

                    // ── Compute how much is credit reduction vs actual cash refund ──
                    double returnValue     = Math.Abs(subTotal);
                    double creditToReduce  = Math.Min(originalBill.RemainingAmount, returnValue);
                    double cashRefund      = Math.Round(returnValue - creditToReduce, 2);
                    creditToReduce         = Math.Round(creditToReduce, 2);

                    // Determine return outcome type for UI labelling
                    string returnOutcome = creditToReduce > 0 && cashRefund > 0 ? "Mixed"
                                        : creditToReduce > 0                   ? "CreditOnly"
                                                                               : "CashOnly";

                    var returnBill = new Bill
                    {
                        BillDateTime    = DateTime.Now,
                        SubTotal        = subTotal,
                        DiscountAmount  = 0,
                        TaxAmount       = 0,
                        GrandTotal      = subTotal,
                        // CashReceived stores the actual cash handed back to the customer
                        CashReceived    = cashRefund,
                        // PaidAmount mirrors cash refund for accounting symmetry
                        PaidAmount      = cashRefund,
                        ChangeGiven     = 0,
                        UserId          = userId,
                        CustomerId      = originalBill.CustomerId,
                        Type            = "Return",
                        ParentBillId    = originalBillId,
                        // Status encodes the outcome type so callers don't need extra queries
                        Status          = returnOutcome
                    };

                    var savedReturnBill = _billRepo.SaveBillInternal(returnBill, returnBillItems, conn, txn);

                    // Tag the saved return bill with outcome metadata for in-memory use
                    savedReturnBill.CashReceived   = cashRefund;
                    savedReturnBill.PaidAmount     = cashRefund;
                    savedReturnBill.RemainingAmount = creditToReduce; // repurposed: credit reduced
                    savedReturnBill.Status         = returnOutcome;

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

                    // ── Atomically Offset Credit INSIDE the transaction ──
                    if (creditToReduce > 0)
                    {
                        double newRemaining = Math.Round(Math.Max(0, originalBill.RemainingAmount - creditToReduce), 2);
                        double newPaid      = Math.Round(originalBill.PaidAmount + creditToReduce, 2);
                        string newStatus    = newRemaining <= 0 ? "Paid"
                                           : newPaid > 0       ? "Partial"
                                                               : "Unpaid";

                        using var creditCmd = conn.CreateCommand();
                        creditCmd.Transaction = txn;
                        creditCmd.CommandText = @"
                            UPDATE Bill
                            SET RemainingAmount = @rem, PaidAmount = @paid, PaymentStatus = @status
                            WHERE bill_id = @id;

                            INSERT INTO CreditPayments (BillId, AmountPaid, PaidAt, Note)
                            VALUES (@id, @offset, @at, @note);";
                        creditCmd.Parameters.AddWithValue("@rem",    newRemaining);
                        creditCmd.Parameters.AddWithValue("@paid",   newPaid);
                        creditCmd.Parameters.AddWithValue("@status", newStatus);
                        creditCmd.Parameters.AddWithValue("@id",     originalBill.BillId);
                        creditCmd.Parameters.AddWithValue("@offset", creditToReduce);
                        creditCmd.Parameters.AddWithValue("@at",     DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        creditCmd.Parameters.AddWithValue("@note",   $"Return offset — Return Bill #{savedReturnBill.InvoiceNumber}");
                        creditCmd.ExecuteNonQuery();

                        AppLogger.Info($"Return credit offset: Bill #{originalBill.BillId} RemainingAmount {originalBill.RemainingAmount} → {newRemaining} (offset Rs.{creditToReduce}, cashRefund Rs.{cashRefund})");
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
