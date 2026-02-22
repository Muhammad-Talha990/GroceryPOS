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
    /// Represents a single item in the billing cart.
    /// </summary>
    public class CartItem : INotifyPropertyChanged
    {
        /// <summary>Barcode (FK to Item.itemId).</summary>
        public string ItemId { get; set; } = string.Empty;

        /// <summary>Item description for display.</summary>
        public string ItemDescription { get; set; } = string.Empty;

        /// <summary>Unit price at time of adding to cart.</summary>
        public double UnitPrice { get; set; }

        private int _quantity = 1;
        /// <summary>Quantity in cart.</summary>
        public int Quantity
        {
            get => _quantity;
            set
            {
                _quantity = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Quantity)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalPrice)));
            }
        }

        /// <summary>Line total: UnitPrice × Quantity.</summary>
        public double TotalPrice => UnitPrice * Quantity;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// ViewModel for the Billing screen.
    /// Handles barcode scanning, cart management, bill calculation, and bill completion.
    /// Uses percentage-based discount and tax per business rules.
    /// </summary>
    public class BillingViewModel : BaseViewModel
    {
        private readonly AuthService _authService;
        private readonly ItemService _itemService;
        private readonly BillService _billService;
        private readonly PrintService _printService;

        public ObservableCollection<CartItem> CartItems { get; set; } = new();
        public ObservableCollection<Item> ItemList { get; set; } = new();

        private Item? _selectedSearchItem;
        public Item? SelectedSearchItem
        {
            get => _selectedSearchItem;
            set
            {
                SetProperty(ref _selectedSearchItem, value);
                if (value != null)
                {
                    AddToCart(value);
                    SelectedSearchItem = null;
                }
            }
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

        private string _discountText = "0";
        public string DiscountText
        {
            get => _discountText;
            set { SetProperty(ref _discountText, value); RecalculateTotal(); }
        }

        private string _taxText = "0";
        public string TaxText
        {
            get => _taxText;
            set { SetProperty(ref _taxText, value); RecalculateTotal(); }
        }

        private double _discountAmount;
        public double DiscountAmount { get => _discountAmount; set => SetProperty(ref _discountAmount, value); }

        private double _taxAmount;
        public double TaxAmount { get => _taxAmount; set => SetProperty(ref _taxAmount, value); }

        private double _grandTotal;
        public double GrandTotal { get => _grandTotal; set => SetProperty(ref _grandTotal, value); }

        private string _cashReceivedText = "0";
        public string CashReceivedText
        {
            get => _cashReceivedText;
            set { SetProperty(ref _cashReceivedText, value); CalculateChange(); }
        }

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
        
        private string _invoiceNumber = "00000";
        public string InvoiceNumber { get => _invoiceNumber; set => SetProperty(ref _invoiceNumber, value); }

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

            CartItems.CollectionChanged += CartItems_CollectionChanged;
            LoadItems();
            LoadNextInvoice();

            _timer = new System.Windows.Threading.DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += (s, e) => CurrentDateTime = DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss");
            _timer.Start();
        }

        private void LoadNextInvoice()
        {
            try
            {
                InvoiceNumber = _billService.GetNextInvoiceNumber();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load next invoice number", ex);
            }
        }

        private void LoadItems()
        {
            ItemList.Clear();
            foreach (var item in _itemService.GetAllItems())
                ItemList.Add(item);
        }

        private void CartItems_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (CartItem item in e.NewItems)
                    item.PropertyChanged += CartItem_PropertyChanged;
            }
            if (e.OldItems != null)
            {
                foreach (CartItem item in e.OldItems)
                    item.PropertyChanged -= CartItem_PropertyChanged;
            }
            RecalculateTotal();
        }

        private void CartItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CartItem.Quantity))
                RecalculateTotal();
        }

        private void ScanBarcode()
        {
            try
            {
                AppLogger.Info($"ScanBarcode: Started. Input: '{BarcodeInput}'");
                if (string.IsNullOrWhiteSpace(BarcodeInput))
                {
                    StatusMessage = "Please enter or scan a barcode.";
                    AppLogger.Warning("ScanBarcode: Empty input.");
                    return;
                }

                var item = _itemService.GetItemByBarcode(BarcodeInput.Trim());
                if (item == null)
                {
                    StatusMessage = $"✗ Item not found for barcode: {BarcodeInput}";
                    AppLogger.Warning($"ScanBarcode: Item not found for barcode: '{BarcodeInput}'");
                    BarcodeInput = string.Empty;
                    return;
                }

                AppLogger.Info($"ScanBarcode: Item found: {item.Description} (Barcode: {item.ItemId})");
                AddToCart(item);
                BarcodeInput = string.Empty;
            }
            catch (Exception ex)
            {
                AppLogger.Error("ScanBarcode failed", ex);
                StatusMessage = $"✗ Error scanning barcode: {ex.Message}";
            }
        }

        private void AddToCart(Item item)
        {
            AppLogger.Info($"AddToCart: Started for item '{item.Description}' (Barcode: {item.ItemId})");
            if (QuantityInput < 1) QuantityInput = 1;

            var existingItem = CartItems.FirstOrDefault(c => c.ItemId == item.ItemId);
            int qtyToAdd = QuantityInput;

            if (existingItem != null)
            {
                AppLogger.Info($"AddToCart: Updating existing item. Current Qty: {existingItem.Quantity}, Adding: {qtyToAdd}");
                existingItem.Quantity += qtyToAdd;
                StatusMessage = $"✓ {item.Description} — Added +{qtyToAdd} (Total: {existingItem.Quantity})";
                QuantityInput = 1;
            }
            else
            {
                AppLogger.Info($"AddToCart: Adding new item. Qty: {qtyToAdd}");
                CartItems.Add(new CartItem
                {
                    ItemId = item.ItemId,
                    ItemDescription = item.Description,
                    UnitPrice = item.SalePrice,
                    Quantity = qtyToAdd
                });
                StatusMessage = $"✓ Added: {item.Description} — Qty: {qtyToAdd} — Rs.{item.SalePrice * qtyToAdd:N0}";
                QuantityInput = 1;
            }
            RecalculateTotal();
            AppLogger.Info($"AddToCart: Completed. Cart Items Count: {CartItems.Count}, GrandTotal: {GrandTotal}");
        }

        private void RemoveFromCart()
        {
            if (SelectedCartItem != null)
            {
                var name = SelectedCartItem.ItemDescription;
                CartItems.Remove(SelectedCartItem);
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
            if (SelectedCartItem != null)
            {
                if (SelectedCartItem.Quantity > 1)
                {
                    SelectedCartItem.Quantity--;
                    RecalculateTotal();
                }
                else
                {
                    RemoveFromCart();
                }
            }
        }

        /// <summary>
        /// Recalculates bill totals using percentage-based discount and tax.
        /// Business Rules:
        ///   SubTotal       = Σ(Quantity × UnitPrice)
        ///   DiscountAmount = SubTotal × (DiscountPercent / 100)
        ///   TaxAmount      = (SubTotal - DiscountAmount) × (TaxPercent / 100)
        ///   GrandTotal     = SubTotal - DiscountAmount + TaxAmount
        /// </summary>
        private void RecalculateTotal()
        {
            try
            {
                SubTotal = CartItems.Sum(c => c.TotalPrice);

                double.TryParse(DiscountText, out var discount);
                double.TryParse(TaxText, out var tax);

                DiscountAmount = Math.Round(discount, 2);
                TaxAmount = Math.Round(tax, 2);
                GrandTotal = Math.Round(SubTotal - DiscountAmount + TaxAmount, 2);

                if (GrandTotal < 0) GrandTotal = 0;
                CalculateChange();
                AppLogger.Info($"RecalculateTotal: SubTotal: {SubTotal}, Discount: {DiscountAmount}, Tax: {TaxAmount}, GrandTotal: {GrandTotal}");
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

        private void CompleteSale()
        {
            try
            {
                if (!CartItems.Any())
                {
                    StatusMessage = "✗ Cart is empty. Add items before completing bill.";
                    return;
                }

                if (!double.TryParse(CashReceivedText, out var cashReceived) || cashReceived < GrandTotal)
                {
                    StatusMessage = "✗ Cash received must be equal to or greater than grand total.";
                    System.Windows.MessageBox.Show(
                        "Insufficient cash received. Please enter an amount equal to or greater than the Grand Total.",
                        "Insufficient Payment",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                double.TryParse(DiscountText, out var discount);
                double.TryParse(TaxText, out var tax);

                var billItems = CartItems.Select(c => new BillDescription
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

                StatusMessage = $"✓ Bill completed! Bill#: {_lastBill.BillId} | Total: Rs.{_lastBill.GrandTotal:N2} | Change: Rs.{_lastBill.ChangeGiven:N2}";

                // Auto print
                try
                {
                    _printService.PrintReceipt(_lastBill, _authService.CurrentUser?.FullName ?? "Cashier");
                }
                catch
                {
                    StatusMessage += " (Printer not available)";
                }

                ClearCart();
                LoadNextInvoice();
                // No need to manually reload all items, they are updated in memory by BillService
            }
            catch (Exception ex)
            {
                StatusMessage = $"✗ Bill failed: {ex.Message}";
                AppLogger.Error("Complete bill failed", ex);
            }
        }

        private void ClearCart()
        {
            try
            {
                CartItems.Clear();
                _discountText = "0"; OnPropertyChanged(nameof(DiscountText));
                _taxText = "0"; OnPropertyChanged(nameof(TaxText));
                _cashReceivedText = "0"; OnPropertyChanged(nameof(CashReceivedText));

                RecalculateTotal();
                StatusMessage = "Cart cleared.";
            }
            catch (Exception ex)
            {
                AppLogger.Error("ClearCart failed", ex);
                StatusMessage = $"✗ Error clearing cart: {ex.Message}";
            }
        }

        private void PrintLastReceipt()
        {
            if (_lastBill == null)
            {
                StatusMessage = "✗ No recent bill to print.";
                return;
            }

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
