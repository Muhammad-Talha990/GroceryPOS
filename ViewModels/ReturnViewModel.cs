using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using GroceryPOS.Helpers;
using GroceryPOS.Models;
using GroceryPOS.Services;
using GroceryPOS.Exceptions;

namespace GroceryPOS.ViewModels
{
    public class ReturnViewModel : BaseViewModel
    {
        private readonly IReturnService _returnService;
        private readonly AuthService _authService;
        private readonly PrintService _printService;

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

        public ObservableCollection<ReturnItemViewModel> Items { get; } = new();
        public ObservableCollection<BillReturn> ReturnHistory { get; } = new();

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _isPreviewVisible;
        public bool IsPreviewVisible
        {
            get => _isPreviewVisible;
            set => SetProperty(ref _isPreviewVisible, value);
        }

        // ── Return Outcome Result (populated after ProcessReturn) ──
        private double _returnResultCashRefund;
        public double ReturnResultCashRefund
        {
            get => _returnResultCashRefund;
            set
            {
                if (SetProperty(ref _returnResultCashRefund, value))
                    OnPropertyChanged(nameof(ShowCashRefundRow));
            }
        }

        private double _returnResultCreditAdjusted;
        public double ReturnResultCreditAdjusted
        {
            get => _returnResultCreditAdjusted;
            set
            {
                if (SetProperty(ref _returnResultCreditAdjusted, value))
                    OnPropertyChanged(nameof(ShowCreditAdjustRow));
            }
        }

        private string _returnResultType = string.Empty;
        public string ReturnResultType
        {
            get => _returnResultType;
            set => SetProperty(ref _returnResultType, value);
        }

        public bool ShowCashRefundRow    => ReturnResultCashRefund > 0;
        public bool ShowCreditAdjustRow  => ReturnResultCreditAdjusted > 0;

        public string StoreName => "GROCERY MART";
        public string StoreAddress => "123 Main Street, City Name";
        public string StorePhone => "0300-1234567";

        public ICommand SearchCommand { get; }
        public ICommand ProcessReturnCommand { get; }
        public ICommand ClearFormCommand { get; }
        public ICommand TogglePreviewCommand { get; }

        // ── Preview Calculation Properties ──
        public decimal CurrentReturnGrandTotal => Items.Sum(i => (decimal)i.ReturnQuantity * (decimal)(OriginalBill?.Items.FirstOrDefault(bi => bi.ItemId == i.ItemId)?.UnitPrice ?? 0));
        
        public double PreviewRemainingDue
        {
            get
            {
                if (OriginalBill == null) return 0;
                double previousReturnsTotal = ReturnHistory.Sum(r => r.ReturnQuantity * r.UnitPrice);
                return OriginalBill.GrandTotal - previousReturnsTotal - (double)CurrentReturnGrandTotal;
            }
        }

        public ObservableCollection<ReturnItemViewModel> CurrentReturnPreviewItems { get; } = new();

        public void RefreshPreview()
        {
            CurrentReturnPreviewItems.Clear();
            foreach (var item in Items.Where(i => i.ReturnQuantity > 0))
            {
                CurrentReturnPreviewItems.Add(item);
            }
            OnPropertyChanged(nameof(CurrentReturnGrandTotal));
            OnPropertyChanged(nameof(PreviewRemainingDue));
        }

        public ReturnViewModel(IReturnService returnService, AuthService authService, PrintService printService)
        {
            _returnService = returnService;
            _authService = authService;
            _printService = printService;

            SearchCommand = new RelayCommand(async _ => await SearchBill());
            ProcessReturnCommand = new RelayCommand(async _ => await ProcessReturn());
            ClearFormCommand = new RelayCommand(_ => ClearForm());
            TogglePreviewCommand = new RelayCommand(_ => IsPreviewVisible = !IsPreviewVisible);
        }

        private async Task SearchBill()
        {
            if (!int.TryParse(BillIdInput, out int billId))
            {
                StatusMessage = "Please enter a valid Bill ID.";
                ShowPopupError("Please enter a valid Bill ID.");
                return;
            }

            try
            {
                StatusMessage = "Searching...";
                var result = await _returnService.GetBillWithReturnHistory(billId);
                OriginalBill = result.Original;

                // Load return history from database (item-level records)
                var history = await _returnService.GetReturnHistory(OriginalBill.BillId);

                Dispatch(() =>
                {
                    Items.Clear();
                    foreach (var item in OriginalBill.Items)
                    {
                        int alreadyReturned = _returnService.GetTotalReturnedQuantity(OriginalBill.BillId, item.ItemId);
                        var vm = new ReturnItemViewModel
                        {
                            ItemId = item.ItemId,
                            Barcode = item.Barcode,
                            Description = item.ItemDescription,
                            OriginalQuantity = item.Quantity,
                            AlreadyReturned = alreadyReturned,
                            RemainingQuantity = item.Quantity - alreadyReturned,
                            ReturnQuantity = 0,
                            UnitPrice = (decimal)item.UnitPrice
                        };
                        Items.Add(vm);
                    }

                    ReturnHistory.Clear();
                    int lastReturnId = -1;
                    int seqIdx = 0;
                    foreach (var entry in history.OrderBy(r => r.ReturnedAt))
                    {
                        if (entry.Id != lastReturnId)
                        {
                            seqIdx++;
                            lastReturnId = entry.Id;
                        }
                        entry.ReturnBillId = $"Return {seqIdx}";
                        ReturnHistory.Add(entry);
                    }
                    RefreshPreview();
                });

                StatusMessage = $"✓ Bill #{billId} loaded with {result.Returns.Count} previous returns.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                ShowPopupError(ex.Message);
                AppLogger.Error("SearchBill failed", ex);
            }
        }

        private async Task ProcessReturn()
        {
            if (OriginalBill == null) return;

            var itemsToReturn = Items.Where(i => i.ReturnQuantity > 0)
                .Select(i => new BillDescription
                {
                    ItemId = i.ItemId,
                    Quantity = i.ReturnQuantity,
                    ItemDescription = i.Description
                }).ToList();

            if (!itemsToReturn.Any())
            {
                StatusMessage = "No items selected for return.";
                ShowPopupError("No items selected for return.");
                return;
            }

            try
            {
                StatusMessage = "Processing return...";
                var returnBill = await _returnService.ProcessReturn(OriginalBill.BillId, _authService.CurrentUser?.Id, itemsToReturn);

                // ── Populate result properties from return bill metadata ──
                double cashRefund       = returnBill.CashReceived;
                double creditAdjusted   = returnBill.RemainingDueAfterThisReturn;
                string outcomeType      = returnBill.Status; // "CashOnly" | "CreditOnly" | "Mixed"

                ReturnResultCashRefund    = cashRefund;
                ReturnResultCreditAdjusted = creditAdjusted;
                ReturnResultType          = outcomeType;

                // Build descriptive success message
                string msg = outcomeType switch
                {
                    "CreditOnly" => $"✓ Return Bill #{returnBill.InvoiceNumber} — Credit reduced by Rs.{creditAdjusted:N0}. No cash refund.",
                    "CashOnly"   => $"✓ Return Bill #{returnBill.InvoiceNumber} — Cash refund: Rs.{cashRefund:N0}",
                    "Mixed"      => $"✓ Return Bill #{returnBill.InvoiceNumber} — Credit reduced Rs.{creditAdjusted:N0} + Cash refund Rs.{cashRefund:N0}",
                    _            => $"✓ Return processed! Return Bill: {returnBill.InvoiceNumber}"
                };
                StatusMessage = msg;
                
                // ── Print Return-Only Receipt ──
                try
                {
                    _printService.PrintReturnOnlyReceipt(OriginalBill, returnBill, _authService.CurrentUser?.FullName ?? "Cashier");
                }
                catch (Exception pex)
                {
                    StatusMessage += " (Print failed)";
                    AppLogger.Error("Return receipt print failed", pex);
                }

                // ── Refresh the current view instead of clearing it ──
                // This lets the user see the updated "Already Returned" quantities and the new history entry.
                await SearchBill();
                
                // Keep the success message visible even after SearchBill updates it
                StatusMessage = $"✓ Return processed! Return Bill: {returnBill.InvoiceNumber}";
            }
            catch (BusinessException ex)
            {
                StatusMessage = $"Business Rule: {ex.Message}";
                ShowPopupError(ex.Message);
            }
            catch (Exception ex)
            {
                StatusMessage = $"An error occurred: {ex.Message}";
                ShowPopupError(ex.Message);
                AppLogger.Error("ProcessReturn failed", ex);
            }
        }


        public void ClearForm()
        {
            OriginalBill = null;
            Dispatch(() => Items.Clear());
            Dispatch(() => ReturnHistory.Clear());
            BillIdInput = string.Empty;
            StatusMessage = "Form cleared.";
        }
    }

    public class ReturnItemViewModel : BaseViewModel
    {
        public string ItemId { get; set; } = string.Empty;
        public string? Barcode { get; set; }
        public string Description { get; set; } = string.Empty;
        public double OriginalQuantity { get; set; }
        public double AlreadyReturned { get; set; }
        public double RemainingQuantity { get; set; }

        private double _returnQuantity;
        public double ReturnQuantity
        {
            get => _returnQuantity;
            set
            {
                if (value > RemainingQuantity) value = RemainingQuantity;
                if (value < 0) value = 0;
                if (SetProperty(ref _returnQuantity, value))
                {
                    OnPropertyChanged(nameof(TotalPrice));
                    if (Application.Current.MainWindow.DataContext is MainViewModel mainVM && 
                        mainVM.CurrentView is ReturnViewModel returnVM)
                    {
                        returnVM.RefreshPreview();
                    }
                }
            }
        }

        public decimal UnitPrice { get; set; }
        public decimal TotalPrice => (decimal)ReturnQuantity * UnitPrice;
    }
}
