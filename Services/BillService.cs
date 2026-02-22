using System;
using System.Collections.Generic;
using System.Linq;
using GroceryPOS.Data.Repositories;
using GroceryPOS.Helpers;
using GroceryPOS.Models;

namespace GroceryPOS.Services
{
    /// <summary>
    /// Business logic for billing operations.
    /// Implements bill calculation rules, validation, and transactional saving.
    /// Replaces the old SaleService.
    /// 
    /// Business Rules:
    ///   SubTotal       = Σ(Quantity × UnitPrice)
    ///   GrandTotal     = SubTotal - DiscountAmount + TaxAmount
    ///   ChangeGiven    = CashReceived - GrandTotal
    /// </summary>
    public class BillService
    {
        private readonly BillRepository _billRepo;
        private readonly DataCacheService _cache;

        public BillService(BillRepository billRepo, DataCacheService cache)
        {
            _billRepo = billRepo;
            _cache = cache;
        }

        // ────────────────────────────────────────────
        //  BILL COMPLETION
        // ────────────────────────────────────────────

        /// <summary>
        /// Validates inputs, calculates totals, and saves the bill atomically.
        /// </summary>
        /// <param name="userId">Cashier user ID (nullable).</param>
        /// <param name="items">Line items with ItemId, Quantity, UnitPrice.</param>
        /// <param name="discountAmount">Flat discount amount.</param>
        /// <param name="taxAmount">Flat tax amount.</param>
        /// <param name="cashReceived">Cash received from customer.</param>
        /// <returns>The completed Bill with generated BillId.</returns>
        public Bill CompleteBill(int? userId, List<BillDescription> items,
            double discountAmount, double taxAmount, double cashReceived)
        {
            // ── Validate inputs ──
            if (items == null || items.Count == 0)
                throw new InvalidOperationException("Cannot complete bill with no items.");

            if (discountAmount < 0)
                throw new ArgumentException("Discount amount cannot be negative.");

            if (taxAmount < 0)
                throw new ArgumentException("Tax amount cannot be negative.");

            // ── Calculate totals per business rules ──
            foreach (var item in items)
                item.TotalPrice = item.Quantity * item.UnitPrice;

            double subTotal = items.Sum(i => i.TotalPrice);
            double grandTotal = Math.Round(subTotal - discountAmount + taxAmount, 2);
            double changeGiven = Math.Round(cashReceived - grandTotal, 2);

            // ── Validate cash ──
            if (cashReceived < grandTotal)
                throw new InvalidOperationException(
                    $"Insufficient cash. Grand Total: Rs.{grandTotal:N2}, Cash Received: Rs.{cashReceived:N2}");

            // ── Build Bill object ──
            var bill = new Bill
            {
                BillDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                SubTotal = subTotal,
                DiscountAmount = discountAmount,
                TaxAmount = taxAmount,
                GrandTotal = grandTotal,
                CashReceived = cashReceived,
                ChangeGiven = changeGiven,
                UserId = userId
            };

            // ── Save atomically (bill + items) ──
            var savedBill = _billRepo.SaveBillWithTransaction(bill, items);
            
            return savedBill;
        }

        // ────────────────────────────────────────────
        //  READ operations
        // ────────────────────────────────────────────

        public List<Bill> GetTodayBills() => _billRepo.GetToday();

        public List<Bill> GetBillsByDateRange(DateTime from, DateTime to) => _billRepo.GetByDateRange(from, to);

        public double GetTodayTotal() => _billRepo.GetTodayTotal();

        public int GetTodayBillCount() => _billRepo.GetTodayCount();

        public Bill? GetBillById(int billId) => _billRepo.GetById(billId);

        public string GetNextInvoiceNumber()
        {
            int nextId = _billRepo.GetNextBillId();
            return nextId.ToString("D5");
        }
    }
}
