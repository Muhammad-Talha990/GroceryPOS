using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using GroceryPOS.Helpers;
using GroceryPOS.Models;
using GroceryPOS.Services;

namespace GroceryPOS.ViewModels
{
    /// <summary>
    /// ViewModel for the Billing screen.
    /// Handles multiple billing tabs, barcode scanning, cart management, bill calculation, and bill completion.
    /// </summary>
    public class BillingViewModel : BaseViewModel
    {
        private readonly AuthService _authService;
        private readonly ItemService _itemService;
        private readonly BillService _billService;
        private readonly PrintService _printService;

        public ObservableCollection<BillingTab> Tabs { get; set; } = new();

        private BillingTab? _selectedTab;
        public BillingTab? SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (SelectedTab != null) SelectedTab.IsActive = false;
                if (SetProperty(ref _selectedTab, value))
                {
                    if (SelectedTab != null) SelectedTab.IsActive = true;
                    NotifyTabPropertiesChanged();
                    RecalculateTotal();
                }
            }
        }

        // Redirecting properties for UI binding to the SelectedTab
        public ObservableCollection<CartItem> CartItems => SelectedTab?.CartItems ?? new();
        public string DiscountText
        {
            get => SelectedTab?.DiscountText ?? "0";
            set { if (SelectedTab != null) { SelectedTab.DiscountText = value; RecalculateTotal(); OnPropertyChanged(); } }
        }
        public string TaxText
        {
            get => SelectedTab?.TaxText ?? "0";
            set { if (SelectedTab != null) { SelectedTab.TaxText = value; RecalculateTotal(); OnPropertyChanged(); } }
        }
        public string CashReceivedText
        {
            get => SelectedTab?.CashReceivedText ?? "0";
            set { if (SelectedTab != null) { SelectedTab.CashReceivedText = value; CalculateChange(); OnPropertyChanged(); } }
        }
        public string InvoiceNumber
        {
            get => SelectedTab?.InvoiceNumber ?? "00000";
            set { if (SelectedTab != null) { SelectedTab.InvoiceNumber = value; OnPropertyChanged(); } }
        }

        public ObservableCollection<Item> ItemList { get; set; } = new();

        private Item? _selectedSearchItem;
        public Item? SelectedSearchItem
        {
            get => _selectedSearchItem;
            set => SetProperty(ref _selectedSearchItem, value);
        }

        private string _barcodeInput = string.Empty;
        public string BarcodeInput
        {
            get => _barcodeInput;
            set
            {
                if (SetProperty(ref _barcodeInput, value))
                {
                    AppLogger.Info($"BarcodeInput changed: '{value}'");
                }
            }
        }

        private int _quantityInput = 1;
        public int QuantityInput
        {
            get => _quantityInput;
            set => SetProperty(ref _quantityInput, value);
        }

        private double _subTotal;
        public double SubTotal { get => _subTotal; set => SetProperty(ref _subTotal, value); }

        private double _discountAmount;
        public double DiscountAmount { get => _discountAmount; set => SetProperty(ref _discountAmount, value); }

        private double _taxAmount;
        public double TaxAmount { get => _taxAmount; set => SetProperty(ref _taxAmount, value); }

        private double _grandTotal;
        public double GrandTotal { get => _grandTotal; set => SetProperty(ref _grandTotal, value); }

        private double _changeAmount;
        public double ChangeAmount 
        { 
            get => _changeAmount; 
            set 
            { 
                if (SetProperty(ref _changeAmount, value))
                    OnPropertyChanged(nameof(IsChangeNegative));
            } 
        }

        public bool IsChangeNegative => ChangeAmount < 0;
        
        private string _currentDateTime = DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss");
        public string CurrentDateTime { get => _currentDateTime; set => SetProperty(ref _currentDateTime, value); }

        private readonly System.Windows.Threading.DispatcherTimer _timer;

        private string _statusMessage = string.Empty;
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private CartItem? _selectedCartItem;
        public CartItem? SelectedCartItem { get => _selectedCartItem; set => SetProperty(ref _selectedCartItem, value); }

        public ICommand ScanBarcodeCommand { get; }
        public ICommand RemoveFromCartCommand { get; }
        public ICommand IncreaseQuantityCommand { get; }
        public ICommand DecreaseQuantityCommand { get; }
        public ICommand CompleteSaleCommand { get; }
        public ICommand ClearCartCommand { get; }
        public ICommand PrintReceiptCommand { get; }
        public ICommand AddTabCommand { get; }
        public ICommand CloseTabCommand { get; }

        private Bill? _lastBill;

        public BillingViewModel(AuthService authService, ItemService itemService, BillService billService, PrintService printService)
        {
            _authService = authService;
            _itemService = itemService;
            _billService = billService;
            _printService = printService;

            ScanBarcodeCommand = new RelayCommand(ScanBarcode);
            RemoveFromCartCommand = new RelayCommand(RemoveFromCart);
            IncreaseQuantityCommand = new RelayCommand(IncreaseQuantity);
            DecreaseQuantityCommand = new RelayCommand(DecreaseQuantity);
            CompleteSaleCommand = new RelayCommand(CompleteSale);
            ClearCartCommand = new RelayCommand(ClearCart);
            PrintReceiptCommand = new RelayCommand(PrintLastReceipt);
            AddTabCommand = new RelayCommand(AddNewTab);
            CloseTabCommand = new RelayCommand(CloseTab);

            LoadItems();
            
            // Initialize with first tab
            AddNewTab();

            _timer = new System.Windows.Threading.DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += (s, e) => CurrentDateTime = DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss");
            _timer.Start();
        }

        private void AddNewTab()
        {
            try
            {
                var nextInvoice = _billService.GetNextInvoiceNumber();
                // Increment if other tabs already have that number
                while (Tabs.Any(t => t.InvoiceNumber == nextInvoice))
                {
                    if (int.TryParse(nextInvoice, out int num))
                        nextInvoice = (num + 1).ToString("D5");
                    else
                        break;
                }

                var newTab = new BillingTab
                {
                    TabName = $"Bill {Tabs.Count + 1}",
                    InvoiceNumber = nextInvoice
                };
                newTab.CartItems.CollectionChanged += (s, e) => {
                    if (e.NewItems != null)
                    {
                        foreach (CartItem item in e.NewItems)
                            item.PropertyChanged += (sender, args) => { if (args.PropertyName == nameof(CartItem.Quantity)) RecalculateTotal(); };
                    }
                    if (SelectedTab == newTab)
                    {
                        RecalculateTotal();
                        OnPropertyChanged(nameof(CartItems));
                    }
                };
                
                Tabs.Add(newTab);
                SelectedTab = newTab;
                StatusMessage = $"Added new tab: {newTab.TabName}";
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to add new tab", ex);
            }
        }

        private void CloseTab(object? parameter)
        {
            if (parameter is BillingTab tab)
            {
                if (Tabs.Count <= 1)
                {
                    // Don't close the last tab, just clear it
                    ClearCart();
                    return;
                }

                Tabs.Remove(tab);
                if (SelectedTab == tab)
                    SelectedTab = Tabs.LastOrDefault();
                
                // Rename remaining tabs for consistency
                for (int i = 0; i < Tabs.Count; i++)
                    Tabs[i].TabName = $"Bill {i + 1}";
            }
        }

        private void NotifyTabPropertiesChanged()
        {
            OnPropertyChanged(nameof(CartItems));
            OnPropertyChanged(nameof(DiscountText));
            OnPropertyChanged(nameof(TaxText));
            OnPropertyChanged(nameof(CashReceivedText));
            OnPropertyChanged(nameof(InvoiceNumber));
        }

        private void LoadItems()
        {
            ItemList.Clear();
            foreach (var item in _itemService.GetAllItems())
                ItemList.Add(item);
        }

        private void RecalculateTotal()
        {
            try
            {
                if (SelectedTab == null) return;

                SubTotal = SelectedTab.CartItems.Sum(c => c.TotalPrice);

                double.TryParse(DiscountText, out var discount);
                double.TryParse(TaxText, out var tax);

                DiscountAmount = Math.Round(discount, 2);
                TaxAmount = Math.Round(tax, 2);
                GrandTotal = Math.Round(SubTotal - DiscountAmount + TaxAmount, 2);

                if (GrandTotal < 0) GrandTotal = 0;
                CalculateChange();
            }
            catch (Exception ex)
            {
                AppLogger.Error("RecalculateTotal failed", ex);
            }
        }

        private void CalculateChange()
        {
            if (double.TryParse(CashReceivedText, out var cash))
                ChangeAmount = Math.Round(cash - GrandTotal, 2);
            else
                ChangeAmount = 0;
        }

        private void ScanBarcode()
        {
            try
            {
                // Prioritize the selected product from search dropdown
                if (SelectedSearchItem != null)
                {
                    AddToCart(SelectedSearchItem);
                    SelectedSearchItem = null;
                    BarcodeInput = string.Empty;
                    return;
                }

                if (string.IsNullOrWhiteSpace(BarcodeInput))
                {
                    StatusMessage = "Please select a product or enter/scan a barcode.";
                    return;
                }

                var item = _itemService.GetItemByBarcode(BarcodeInput.Trim());
                if (item == null)
                {
                    StatusMessage = $"✗ Item not found for barcode: {BarcodeInput}";
                    BarcodeInput = string.Empty;
                    return;
                }

                AddToCart(item);
                BarcodeInput = string.Empty;
            }
            catch (Exception ex)
            {
                AppLogger.Error("ScanBarcode failed", ex);
                StatusMessage = $"✗ Error adding item: {ex.Message}";
            }
        }

        private void AddToCart(Item item)
        {
            if (SelectedTab == null) return;
            if (QuantityInput < 1) QuantityInput = 1;

            var existingItem = SelectedTab.CartItems.FirstOrDefault(c => c.ItemId == item.ItemId);
            int qtyToAdd = QuantityInput;

            if (existingItem != null)
            {
                existingItem.Quantity += qtyToAdd;
                StatusMessage = $"✓ {item.Description} — Added +{qtyToAdd} (Total: {existingItem.Quantity})";
            }
            else
            {
                SelectedTab.CartItems.Add(new CartItem
                {
                    ItemId = item.ItemId,
                    ItemDescription = item.Description,
                    UnitPrice = item.SalePrice,
                    Quantity = qtyToAdd
                });
                StatusMessage = $"✓ Added: {item.Description} — Qty: {qtyToAdd}";
            }
            QuantityInput = 1;
            RecalculateTotal();
        }

        private void RemoveFromCart()
        {
            if (SelectedCartItem != null && SelectedTab != null)
            {
                var name = SelectedCartItem.ItemDescription;
                SelectedTab.CartItems.Remove(SelectedCartItem);
                StatusMessage = $"Removed: {name}";
                RecalculateTotal();
            }
        }

        private void IncreaseQuantity()
        {
            if (SelectedCartItem != null)
            {
                SelectedCartItem.Quantity++;
                RecalculateTotal();
            }
        }

        private void DecreaseQuantity()
        {
            if (SelectedCartItem != null && SelectedTab != null)
            {
                if (SelectedCartItem.Quantity > 1)
                {
                    SelectedCartItem.Quantity--;
                }
                else
                {
                    SelectedTab.CartItems.Remove(SelectedCartItem);
                }
                RecalculateTotal();
            }
        }

        private void CompleteSale()
        {
            try
            {
                if (SelectedTab == null || !SelectedTab.CartItems.Any())
                {
                    StatusMessage = "✗ Cart is empty.";
                    return;
                }

                if (!double.TryParse(CashReceivedText, out var cashReceived) || cashReceived < GrandTotal)
                {
                    StatusMessage = "✗ Insufficient cash.";
                    return;
                }

                double.TryParse(DiscountText, out var discount);
                double.TryParse(TaxText, out var tax);

                var billItems = SelectedTab.CartItems.Select(c => new BillDescription
                {
                    ItemId = c.ItemId,
                    Quantity = c.Quantity,
                    UnitPrice = c.UnitPrice,
                    TotalPrice = c.TotalPrice,
                    ItemDescription = c.ItemDescription
                }).ToList();

                _lastBill = _billService.CompleteBill(
                    _authService.CurrentUser?.Id,
                    billItems,
                    discount,
                    tax,
                    cashReceived
                );

                StatusMessage = $"✓ Bill completed! Bill#: {_lastBill.BillId}";

                try
                {
                    _printService.PrintReceipt(_lastBill, _authService.CurrentUser?.FullName ?? "Cashier");
                }
                catch { }

                // After complete, remove the tab if it's not the only one, or clear it
                if (Tabs.Count > 1)
                {
                    var tabToClose = SelectedTab;
                    SelectedTab = Tabs.FirstOrDefault(t => t != tabToClose);
                    Tabs.Remove(tabToClose);
                    // Rename remaining tabs
                    for (int i = 0; i < Tabs.Count; i++)
                        Tabs[i].TabName = $"Bill {i + 1}";
                }
                else
                {
                    ClearCart();
                    // Update invoice number for the cleared tab
                    InvoiceNumber = _billService.GetNextInvoiceNumber();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"✗ Bill failed: {ex.Message}";
                AppLogger.Error("Complete bill failed", ex);
            }
        }

        private void ClearCart()
        {
            if (SelectedTab == null) return;
            SelectedTab.CartItems.Clear();
            SelectedTab.DiscountText = "0";
            SelectedTab.TaxText = "0";
            SelectedTab.CashReceivedText = "0";
            NotifyTabPropertiesChanged();
            RecalculateTotal();
            StatusMessage = "Cart cleared.";
        }

        private void PrintLastReceipt()
        {
            if (_lastBill == null) return;
            try
            {
                _printService.PrintReceipt(_lastBill, _authService.CurrentUser?.FullName ?? "Cashier");
                StatusMessage = "✓ Receipt printed!";
            }
            catch (Exception ex)
            {
                StatusMessage = $"✗ Print failed: {ex.Message}";
            }
        }
    }
}
