using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using GroceryPOS.Helpers;
using GroceryPOS.Models;
using GroceryPOS.Services;
using GroceryPOS.Exceptions;

namespace GroceryPOS.ViewModels
{
    public class UpdateBillViewModel : BaseViewModel
    {
        private readonly IBillUpdateService _updateService;
        private readonly BillService _billService;
        private readonly AuthService _authService;
        private readonly ItemService _itemService;

        private string _billIdInput = string.Empty;
        public string BillIdInput
        {
            get => _billIdInput;
            set => SetProperty(ref _billIdInput, value);
        }

        private Bill? _originalBill;
        public Bill? OriginalBill
        {
            get => _originalBill;
            set => SetProperty(ref _originalBill, value);
        }

        public ObservableCollection<BillDescription> Items { get; } = new();

        private double _discount;
        public double Discount
        {
            get => _discount;
            set { if (SetProperty(ref _discount, value)) Recalculate(); }
        }

        private double _tax;
        public double Tax
        {
            get => _tax;
            set { if (SetProperty(ref _tax, value)) Recalculate(); }
        }

        private double _cashReceived;
        public double CashReceived
        {
            get => _cashReceived;
            set { if (SetProperty(ref _cashReceived, value)) Recalculate(); }
        }

        private double _subTotal;
        public double SubTotal { get => _subTotal; set => SetProperty(ref _subTotal, value); }

        private double _grandTotal;
        public double GrandTotal { get => _grandTotal; set => SetProperty(ref _grandTotal, value); }

        private double _changeAmount;
        public double ChangeAmount { get => _changeAmount; set => SetProperty(ref _changeAmount, value); }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ICommand LoadBillCommand { get; }
        public ICommand UpdateBillCommand { get; }
        public ICommand RemoveItemCommand { get; }
        public ICommand ClearFormCommand { get; }

        public UpdateBillViewModel(IBillUpdateService updateService, BillService billService, AuthService authService, ItemService itemService)
        {
            _updateService = updateService;
            _billService = billService;
            _authService = authService;
            _itemService = itemService;

            LoadBillCommand = new RelayCommand(async _ => await LoadBill());
            UpdateBillCommand = new RelayCommand(async _ => await ExecuteUpdate());
            RemoveItemCommand = new RelayCommand(p => RemoveItem(p as BillDescription));
            ClearFormCommand = new RelayCommand(_ => ClearForm());
        }

        private async Task LoadBill()
        {
            if (!int.TryParse(BillIdInput, out int billId))
            {
                StatusMessage = "Please enter a valid Bill ID.";
                return;
            }

            try
            {
                var bill = await Task.Run(() => _billService.GetBillById(billId));
                if (bill == null)
                {
                    StatusMessage = $"Bill #{billId} not found.";
                    return;
                }

                OriginalBill = bill;
                Items.Clear();
                foreach (var item in bill.Items)
                {
                    Items.Add(new BillDescription
                    {
                        ItemId = item.ItemId,
                        ItemDescription = item.ItemDescription,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice,
                        TotalPrice = item.TotalPrice
                    });
                }

                Discount = bill.DiscountAmount;
                Tax = bill.TaxAmount;
                CashReceived = bill.CashReceived;

                StatusMessage = $"Loaded Bill #{billId} for editing.";
                Recalculate();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                AppLogger.Error("LoadBill failed", ex);
            }
        }

        private void RemoveItem(BillDescription? item)
        {
            if (item != null)
            {
                Items.Remove(item);
                Recalculate();
            }
        }

        private void ClearForm()
        {
            BillIdInput = string.Empty;
            OriginalBill = null;
            Items.Clear();
            Discount = 0;
            Tax = 0;
            CashReceived = 0;
            SubTotal = 0;
            GrandTotal = 0;
            ChangeAmount = 0;
            StatusMessage = "Form cleared.";
        }

        private void Recalculate()
        {
            SubTotal = Items.Sum(i => i.Quantity * i.UnitPrice);
            GrandTotal = Math.Round(SubTotal - Discount + Tax, 2);
            ChangeAmount = Math.Round(CashReceived - GrandTotal, 2);
        }

        private async Task ExecuteUpdate()
        {
            if (OriginalBill == null) return;

            try
            {
                StatusMessage = "Updating bill...";
                var newBill = await _updateService.UpdateBill(
                    OriginalBill.BillId, 
                    Items.ToList(), 
                    Discount, 
                    Tax, 
                    CashReceived, 
                    _authService.CurrentUser?.Id
                );

                StatusMessage = $"✓ Bill Updated! New Bill ID: {newBill.BillId}";
                // Optionally clear or reload
            }
            catch (BusinessException ex)
            {
                StatusMessage = $"Business Rule: {ex.Message}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"An error occurred: {ex.Message}";
                AppLogger.Error("ExecuteUpdate failed", ex);
            }
        }
    }
}
