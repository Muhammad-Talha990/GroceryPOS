using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Threading.Tasks;
using GroceryPOS.Helpers;
using GroceryPOS.Models;
using GroceryPOS.Services;
using GroceryPOS.Data.Repositories;

namespace GroceryPOS.ViewModels
{
    public class BillingViewModel : BaseViewModel
    {
        private readonly AuthService _authService;
        private readonly ItemService _itemService;
        private readonly BillService _billService;
        private readonly PrintService _printService;
        private readonly IStockService _stockService;
        private readonly CustomerService _customerService;
        private readonly BillRepository _billRepo;
        private readonly System.Windows.Threading.DispatcherTimer _timer;

        public ObservableCollection<BillingTab> Tabs { get; set; } = new();
        private BillingTab? _selectedTab;
        public BillingTab? SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (_selectedTab != null)
                {
                    _selectedTab.IsActive = false;
                    _selectedTab.CartItems.CollectionChanged -= OnCartItemsCollectionChanged;
                    foreach (var item in _selectedTab.CartItems) item.PropertyChanged -= OnCartItemPropertyChanged;
                }
                
                if (SetProperty(ref _selectedTab, value))
                {
                    if (_selectedTab != null)
                    {
                        _selectedTab.IsActive = true;
                        _selectedTab.CartItems.CollectionChanged += OnCartItemsCollectionChanged;
                        foreach (var item in _selectedTab.CartItems) item.PropertyChanged += OnCartItemPropertyChanged;
                    }
                    NotifyTabPropertiesChanged();
                    RecalculateTotal();
                }
            }
        }

        public ObservableCollection<CartItem> CartItems => SelectedTab?.CartItems ?? new();
        public string DiscountText { get => SelectedTab?.DiscountText ?? "0"; set { if (SelectedTab != null) { SelectedTab.DiscountText = value; RecalculateTotal(); OnPropertyChanged(); } } }
        public string TaxText { get => SelectedTab?.TaxText ?? "0"; set { if (SelectedTab != null) { SelectedTab.TaxText = value; RecalculateTotal(); OnPropertyChanged(); } } }
        public string CashReceivedText { get => SelectedTab?.CashReceivedText ?? "0"; set { if (SelectedTab != null) { SelectedTab.CashReceivedText = value; CalculateChange(); OnPropertyChanged(); } } }
        public string InvoiceNumber { get => SelectedTab?.InvoiceNumber ?? "00000"; set { if (SelectedTab != null) { SelectedTab.InvoiceNumber = value; OnPropertyChanged(); } } }

        public Customer? SelectedCustomer { get => SelectedTab?.Customer; set { if (SelectedTab != null) { SelectedTab.Customer = value; SelectedTab.CustomerId = value?.CustomerId; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedCustomer)); OnPropertyChanged(nameof(IsWalkIn)); } } }
        public bool HasSelectedCustomer => SelectedCustomer != null;
        public bool IsWalkIn => SelectedCustomer == null;

        // ── Credit / Udhar ──
        private double _pendingCreditAmount;
        public double PendingCreditAmount
        {
            get => _pendingCreditAmount;
            set { SetProperty(ref _pendingCreditAmount, value); OnPropertyChanged(nameof(HasPendingCredit)); OnPropertyChanged(nameof(PendingCreditDisplay)); }
        }
        public bool HasPendingCredit => PendingCreditAmount > 0 && HasSelectedCustomer;
        public string PendingCreditDisplay => $"⚠ This customer has Rs. {PendingCreditAmount:N0} pending.";

        public string PaidAmountText { get => SelectedTab?.PaidAmountText ?? string.Empty; set { if (SelectedTab != null) { SelectedTab.PaidAmountText = value; OnPropertyChanged(); } } }

        public string CustomerSearchQuery { get => SelectedTab?.CustomerSearchQuery ?? string.Empty; set { if (SelectedTab != null && SelectedTab.CustomerSearchQuery != value) { SelectedTab.CustomerSearchQuery = value; SearchCustomers(); OnPropertyChanged(); } } }
        public ObservableCollection<Customer> CustomerSearchResults => SelectedTab?.CustomerSearchResults ?? new();
        public ObservableCollection<Bill> CustomerBills => SelectedTab?.CustomerBills ?? new();
        public Customer? SelectedSearchResult { get => SelectedTab?.SelectedSearchResult; set { if (SelectedTab != null) { SelectedTab.SelectedSearchResult = value; OnPropertyChanged(); } } }
        private Bill? _selectedHistoryBill;
        public Bill? SelectedHistoryBill { get => _selectedHistoryBill; set { if (SetProperty(ref _selectedHistoryBill, value) && value != null) { LoadBillIntoCart(value); _selectedHistoryBill = null; OnPropertyChanged(); } } }

        public bool IsCustomerSearchFocused { get; set; }
        public bool IsRegistrationVisible { get; set; }
        public string NewCustomerName { get; set; } = "";
        public string NewCustomerPhone { get; set; } = "";
        public string NewCustomerSecondaryPhone { get; set; } = "";
        public string NewCustomerAddress { get; set; } = "";
        public string RegistrationErrorMessage { get; set; } = "";

        public ObservableCollection<Item> ItemList { get; set; } = new();
        public ObservableCollection<Item> FilteredItemList { get; set; } = new();
        
        private string _productSearchText = "";
        public string ProductSearchText
        {
            get => _productSearchText;
            set 
            { 
                if (SetProperty(ref _productSearchText, value)) 
                { 
                    // If the text change matches the selected item's description, 
                    // it's likely just arrow-key browsing. Skip filtering to keep the full list.
                    if (SelectedSearchItem != null && SelectedSearchItem.Description == value)
                    {
                        return;
                    }

                    FilterProducts();
                    // Open dropdown if we have text and matches
                    IsProductDropDownOpen = !string.IsNullOrWhiteSpace(value) && FilteredItemList.Any();
                } 
            }
        }

        private bool _isProductDropDownOpen;
        public bool IsProductDropDownOpen
        {
            get => _isProductDropDownOpen;
            set => SetProperty(ref _isProductDropDownOpen, value);
        }

        private Item? _selectedSearchItem;
        public Item? SelectedSearchItem
        {
            get => _selectedSearchItem;
            set => SetProperty(ref _selectedSearchItem, value);
        }
        public string BarcodeInput { get; set; } = "";
        public int QuantityInput { get; set; } = 1;

        public double SubTotal { get; set; }
        public double DiscountAmount { get; set; }
        public double TaxAmount { get; set; }
        public double GrandTotal { get; set; }
        public double ChangeAmount { get; set; }
        public bool IsChangeNegative => ChangeAmount < -0.01;
        public bool IsChangeAmountVisible => !IsChangeNegative;
        
        public string ChangeDisplayLabel 
        { 
            get 
            {
                if (IsChangeNegative && IsWalkIn) return "INSUFFICIENT CASH";
                if (IsChangeNegative && HasSelectedCustomer) return "CREDIT / DUE";
                return "RETURN AMOUNT";
            }
        }

        public string ChangeDisplayBrush
        {
            get
            {
                if (IsChangeNegative) return "#EF4444"; // Red
                return "#3B82F6"; // Blue/Accent for return
            }
        }
        public string CurrentDateTime => DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss");
        public DateTime CurrentTime => DateTime.Now;
        public string StatusMessage { get; set; } = "";
        public bool IsPreviewVisible { get; set; }
        public string StoreName => "GROCERY MART";
        public string StoreAddress => "Rawat, Rawalpindi, Pakistan";
        public string StorePhone => "0300-1234567";
        public CartItem? SelectedCartItem { get; set; }
        private Bill? _lastBill;

        public ICommand ScanBarcodeCommand { get; }
        public ICommand RemoveFromCartCommand { get; }
        public ICommand IncreaseQuantityCommand { get; }
        public ICommand DecreaseQuantityCommand { get; }
        public ICommand CompleteSaleCommand { get; }
        public ICommand ClearCartCommand { get; }
        public ICommand PrintReceiptCommand { get; }
        public ICommand AddTabCommand { get; }
        public ICommand CloseTabCommand { get; }
        public ICommand TogglePreviewCommand { get; }
        public ICommand SelectCustomerCommand { get; }
        public ICommand ClearCustomerCommand { get; }
        public ICommand RepeatOrderCommand { get; }
        public ICommand ToggleRegistrationCommand { get; }
        public ICommand SaveNewCustomerCommand { get; }
        public ICommand NavigateSearchCommand { get; }

        public BillingViewModel(AuthService authService, ItemService itemService, BillService billService, PrintService printService, IStockService stockService, CustomerService customerService, BillRepository billRepo)
        {
            _authService = authService; _itemService = itemService; _billService = billService; _printService = printService; _stockService = stockService; _customerService = customerService; _billRepo = billRepo;
            Tabs = new ObservableCollection<BillingTab>(); AddNewTab();
            _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => { OnPropertyChanged(nameof(CurrentTime)); OnPropertyChanged(nameof(CurrentDateTime)); };
            _timer.Start();

            ScanBarcodeCommand = new RelayCommand(_ => ScanBarcode());
            RemoveFromCartCommand = new RelayCommand(_ => RemoveFromCart());
            IncreaseQuantityCommand = new RelayCommand(_ => IncreaseQuantity());
            DecreaseQuantityCommand = new RelayCommand(_ => DecreaseQuantity());
            CompleteSaleCommand = new RelayCommand(_ => CompleteSale());
            ClearCartCommand = new RelayCommand(_ => ClearCart());
            PrintReceiptCommand = new RelayCommand(_ => PrintLastReceipt());
            AddTabCommand = new RelayCommand(_ => AddNewTab());
            CloseTabCommand = new RelayCommand(obj => CloseTab(obj as BillingTab));
            TogglePreviewCommand = new RelayCommand(() => { IsPreviewVisible = !IsPreviewVisible; OnPropertyChanged(nameof(IsPreviewVisible)); });
            SelectCustomerCommand = new RelayCommand(obj => SelectCustomer(obj as Customer));
            ClearCustomerCommand = new RelayCommand(_ => ClearCustomer());
            RepeatOrderCommand = new RelayCommand(_ => RepeatLastOrder(), _ => HasSelectedCustomer);
            ToggleRegistrationCommand = new RelayCommand(() => { IsRegistrationVisible = !IsRegistrationVisible; ClearRegistrationForm(); OnPropertyChanged(nameof(IsRegistrationVisible)); });
            SaveNewCustomerCommand = new RelayCommand(_ => SaveNewCustomer());
            NavigateSearchCommand = new RelayCommand(p => NavigateSearchResults(p?.ToString()));
            LoadProducts();
        }

        private void LoadProducts() { var items = _itemService.GetAllItems(); ItemList = new ObservableCollection<Item>(items); FilteredItemList = new ObservableCollection<Item>(items); }
        private void FilterProducts()
        {
            var query = ProductSearchText ?? "";
            var filtered = string.IsNullOrWhiteSpace(query) 
                ? ItemList.ToList() 
                : ItemList.Where(i => i.Description.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            // Stabilize UI by modifying the collection instead of reassigning it
            var toRemove = FilteredItemList.Where(i => !filtered.Contains(i)).ToList();
            foreach (var item in toRemove) FilteredItemList.Remove(item);

            for (int i = 0; i < filtered.Count; i++)
            {
                if (i >= FilteredItemList.Count || FilteredItemList[i] != filtered[i])
                {
                    if (i < FilteredItemList.Count) FilteredItemList.Insert(i, filtered[i]);
                    else FilteredItemList.Add(filtered[i]);
                }
            }
        }
        private void AddNewTab() { var tab = new BillingTab { TabName = $"Bill {Tabs.Count + 1}", InvoiceNumber = _billService.GetNextInvoiceNumber() }; Tabs.Add(tab); SelectedTab = tab; }
        private void CloseTab(BillingTab? tab) { if (tab == null || Tabs.Count <= 1) return; Tabs.Remove(tab); SelectedTab = Tabs.LastOrDefault(); for (int i = 0; i < Tabs.Count; i++) Tabs[i].TabName = $"Bill {i + 1}"; NotifyTabPropertiesChanged(); }
        private void ScanBarcode() 
        { 
            string bc = !string.IsNullOrWhiteSpace(BarcodeInput) ? BarcodeInput : SelectedSearchItem?.ItemId ?? ""; 
            if (string.IsNullOrWhiteSpace(bc)) return; 
            
            var it = _itemService.GetItemByBarcode(bc); 
            if (it != null) 
            { 
                AddToCart(it); 
                BarcodeInput = ""; 
                SelectedSearchItem = null; 
                ProductSearchText = string.Empty;
                IsProductDropDownOpen = false;
                OnPropertyChanged(nameof(BarcodeInput)); 
                OnPropertyChanged(nameof(ProductSearchText));
                OnPropertyChanged(nameof(IsProductDropDownOpen));
            } 
            else { StatusMessage = "✗ Item not found."; OnPropertyChanged(nameof(StatusMessage)); } 
        }
        private void AddToCart(Item it) { if (SelectedTab == null) return; var ex = SelectedTab.CartItems.FirstOrDefault(i => i.ItemId == it.ItemId); if (ex != null) ex.Quantity += QuantityInput;            else SelectedTab.CartItems.Add(new CartItem { ItemId = it.ItemId, ItemDescription = it.Description, UnitPrice = it.SalePrice, Quantity = QuantityInput });
            QuantityInput = 1; RecalculateTotal(); }
        private void RemoveFromCart() { if (SelectedCartItem != null && SelectedTab != null) { SelectedTab.CartItems.Remove(SelectedCartItem); RecalculateTotal(); } }
        private void IncreaseQuantity() { if (SelectedCartItem != null) { SelectedCartItem.Quantity++; RecalculateTotal(); } }
        private void DecreaseQuantity() { if (SelectedCartItem != null && SelectedCartItem.Quantity > 1) { SelectedCartItem.Quantity--; RecalculateTotal(); } }
        private void RecalculateTotal() { if (SelectedTab == null) return; SubTotal = SelectedTab.CartItems.Sum(i => i.TotalPrice); double.TryParse(DiscountText, out var d); double.TryParse(TaxText, out var t); DiscountAmount = d; TaxAmount = t; GrandTotal = SubTotal - DiscountAmount + TaxAmount; CalculateChange(); OnPropertyChanged(nameof(SubTotal)); OnPropertyChanged(nameof(DiscountAmount)); OnPropertyChanged(nameof(TaxAmount)); OnPropertyChanged(nameof(GrandTotal)); OnPropertyChanged(nameof(CartItems)); }
        private void CalculateChange() { if (double.TryParse(CashReceivedText, out var c)) ChangeAmount = c - GrandTotal; else ChangeAmount = -GrandTotal; OnPropertyChanged(nameof(ChangeAmount)); OnPropertyChanged(nameof(ChangeAmountAbs)); OnPropertyChanged(nameof(IsChangeNegative)); OnPropertyChanged(nameof(IsChangeAmountVisible)); OnPropertyChanged(nameof(ChangeDisplayLabel)); OnPropertyChanged(nameof(ChangeDisplayBrush)); }
        public double ChangeAmountAbs => Math.Abs(ChangeAmount);
        private async void CompleteSale() 
        { 
            try 
            { 
                if (SelectedTab == null || !SelectedTab.CartItems.Any()) { StatusMessage = "✗ Cart is empty."; OnPropertyChanged(nameof(StatusMessage)); return; } 

                double.TryParse(DiscountText, out var d); 
                double.TryParse(TaxText, out var t);
                double sub     = SelectedTab.CartItems.Sum(i => i.TotalPrice);
                double grand   = Math.Round(sub - d + t, 2);

                double.TryParse(CashReceivedText, out var cashReceived);

                // Parse paid amount — if empty, use cash received (capped at grand total)
                double paidAmount;
                if (!string.IsNullOrWhiteSpace(PaidAmountText) && double.TryParse(PaidAmountText, out var pa))
                {
                    paidAmount = pa;
                }
                else
                {
                    // Default to cash received for customers, capped at grand total
                    paidAmount = Math.Min(cashReceived, grand);
                }

                // For walk-in customers, enforce full payment
                if (IsWalkIn)
                {
                    if (cashReceived < grand - 0.01)
                    { StatusMessage = "✗ Insufficient cash."; OnPropertyChanged(nameof(StatusMessage)); return; }
                    paidAmount = grand; 
                }

                var sb = _billService.CompleteBill(_authService.CurrentUser?.Id, SelectedCustomer?.CustomerId, SelectedTab.CartItems.Select(c => new Models.BillDescription { ItemId = c.ItemId, Quantity = c.Quantity, UnitPrice = c.UnitPrice, ItemDescription = c.ItemDescription }).ToList(), d, t, cashReceived, paidAmount);
                _lastBill = sb;
                await AttemptPrint(sb);
                StatusMessage = $"✓ Sale Completed: Bill #{sb.InvoiceNumber} | {sb.PaymentStatus}";
                OnPropertyChanged(nameof(StatusMessage));
                if (Tabs.Count > 1) CloseTab(SelectedTab); else ClearCart(); 
            } 
            catch (Exception ex) { StatusMessage = $"✗ Bill failed: {ex.Message}"; OnPropertyChanged(nameof(StatusMessage)); AppLogger.Error("Complete bill failed", ex); } 
        }
        private async Task AttemptPrint(Bill b) { b.PrintAttempts++; if (!_printService.IsPrinterOnline()) { if (MessageBox.Show("Printer offline. Retry?", "Printer Error", MessageBoxButton.YesNo) == MessageBoxResult.Yes) await AttemptPrint(b); else _billRepo.UpdatePrintStatus(b.BillId, false, null, b.PrintAttempts); return; } if (_printService.PrintReceipt(b, _authService.CurrentUser?.FullName ?? "Cashier")) _billRepo.UpdatePrintStatus(b.BillId, true, DateTime.Now, b.PrintAttempts); else _billRepo.UpdatePrintStatus(b.BillId, false, null, b.PrintAttempts); }
        private void ClearCart() 
        { 
            if (SelectedTab == null) return; 
            SelectedTab.CartItems.Clear(); 
            SelectedTab.DiscountText = "0"; 
            SelectedTab.TaxText = "0"; 
            SelectedTab.CashReceivedText = "0"; 
            SelectedTab.PaidAmountText = string.Empty; 
            PendingCreditAmount = 0; 
            ClearCustomer(); 
            RecalculateTotal(); 
            
            // Ensure UI is notified of reset fields
            OnPropertyChanged(nameof(CashReceivedText));
            OnPropertyChanged(nameof(DiscountText));
            OnPropertyChanged(nameof(TaxText));
            OnPropertyChanged(nameof(PaidAmountText));
            
            InvoiceNumber = _billService.GetNextInvoiceNumber(); 
        }
        private void PrintLastReceipt() { if (_lastBill != null) _ = AttemptPrint(_lastBill); }
        private void SearchCustomers() { if (SelectedTab == null) return; SelectedSearchResult = null; if (string.IsNullOrWhiteSpace(CustomerSearchQuery) || CustomerSearchQuery.Length < 1) { SelectedTab.CustomerSearchResults.Clear(); OnPropertyChanged(nameof(CustomerSearchResults)); return; } var results = _customerService.SearchCustomers(CustomerSearchQuery); SelectedTab.CustomerSearchResults.Clear(); foreach (var c in results) SelectedTab.CustomerSearchResults.Add(c); OnPropertyChanged(nameof(CustomerSearchResults)); }
        private void SelectCustomer(Customer? c)
        {
            var targetCustomer = c ?? SelectedSearchResult;
            if (targetCustomer == null || SelectedTab == null) return;
            
            SelectedCustomer = targetCustomer;
            SelectedTab.CustomerSearchQuery = "";
            SelectedTab.CustomerSearchResults.Clear();
            SelectedSearchResult = null;
            OnPropertyChanged(nameof(CustomerSearchQuery));
            OnPropertyChanged(nameof(CustomerSearchResults));
            LoadCustomerHistory(targetCustomer.CustomerId);

            // Load pending credit for warning badge
            PendingCreditAmount = _customerService.GetPendingCredit(targetCustomer.CustomerId);
        }
        private void ClearCustomer() { if (SelectedTab == null) return; SelectedCustomer = null; SelectedTab.CustomerBills.Clear(); SelectedTab.CustomerSearchQuery = ""; SelectedTab.CustomerSearchResults.Clear(); PendingCreditAmount = 0; OnPropertyChanged(nameof(CustomerSearchQuery)); OnPropertyChanged(nameof(CustomerSearchResults)); OnPropertyChanged(nameof(IsWalkIn)); }
        private void LoadCustomerHistory(int id) { if (SelectedTab == null) return; var bills = _billRepo.GetBillsByCustomerId(id); SelectedTab.CustomerBills.Clear(); foreach (var b in bills) SelectedTab.CustomerBills.Add(b); OnPropertyChanged(nameof(CustomerBills)); }
        private void RepeatLastOrder() { var lb = CustomerBills.FirstOrDefault(); if (lb != null) LoadBillIntoCart(lb); }
        private void LoadBillIntoCart(Bill b) { if (SelectedTab == null) return; SelectedTab.CartItems.Clear(); foreach (var it in b.Items) SelectedTab.CartItems.Add(new CartItem { ItemId = it.ItemId, ItemDescription = it.ItemDescription, UnitPrice = it.UnitPrice, Quantity = it.Quantity }); RecalculateTotal(); }
        private void SaveNewCustomer() { try { if (string.IsNullOrWhiteSpace(NewCustomerName) || string.IsNullOrWhiteSpace(NewCustomerPhone)) { RegistrationErrorMessage = "Name and Phone are required."; OnPropertyChanged(nameof(RegistrationErrorMessage)); return; } 
            var customer = new Customer { Name = NewCustomerName, PrimaryPhone = NewCustomerPhone, SecondaryPhone = NewCustomerSecondaryPhone, Address = NewCustomerAddress };
            _customerService.RegisterCustomer(customer);
            SelectCustomer(customer); 
            SelectCustomer(customer);
            IsRegistrationVisible = false; OnPropertyChanged(nameof(IsRegistrationVisible)); ClearRegistrationForm(); }
            catch (Exception ex) { RegistrationErrorMessage = ex.Message; OnPropertyChanged(nameof(RegistrationErrorMessage)); } }
        private void ClearRegistrationForm()
        {
            NewCustomerName = "";
            NewCustomerPhone = "";
            NewCustomerSecondaryPhone = "";
            NewCustomerAddress = "";
            RegistrationErrorMessage = "";
            OnPropertyChanged(nameof(NewCustomerName));
            OnPropertyChanged(nameof(NewCustomerPhone));
            OnPropertyChanged(nameof(NewCustomerSecondaryPhone));
            OnPropertyChanged(nameof(NewCustomerAddress));
            OnPropertyChanged(nameof(RegistrationErrorMessage));
        }

        private void NavigateSearchResults(string? direction)
        {
            if (CustomerSearchResults == null || CustomerSearchResults.Count == 0) return;

            int currentIndex = SelectedSearchResult != null ? CustomerSearchResults.IndexOf(SelectedSearchResult) : -1;
            int nextIndex = currentIndex;

            if (direction == "Down")
            {
                nextIndex = (currentIndex + 1) % CustomerSearchResults.Count;
            }
            else if (direction == "Up")
            {
                nextIndex = currentIndex <= 0 ? CustomerSearchResults.Count - 1 : currentIndex - 1;
            }

            if (nextIndex >= 0 && nextIndex < CustomerSearchResults.Count)
            {
                SelectedSearchResult = CustomerSearchResults[nextIndex];
            }
        }

        private void OnCartItemsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (CartItem item in e.OldItems) item.PropertyChanged -= OnCartItemPropertyChanged;
            }
            if (e.NewItems != null)
            {
                foreach (CartItem item in e.NewItems) item.PropertyChanged += OnCartItemPropertyChanged;
            }
            RecalculateTotal();
        }

        private void OnCartItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CartItem.Quantity) || e.PropertyName == nameof(CartItem.TotalPrice))
            {
                RecalculateTotal();
            }
        }

        private void NotifyTabPropertiesChanged() { OnPropertyChanged(nameof(CartItems)); OnPropertyChanged(nameof(DiscountText)); OnPropertyChanged(nameof(TaxText)); OnPropertyChanged(nameof(CashReceivedText)); OnPropertyChanged(nameof(PaidAmountText)); OnPropertyChanged(nameof(InvoiceNumber)); OnPropertyChanged(nameof(SelectedCustomer)); OnPropertyChanged(nameof(HasSelectedCustomer)); OnPropertyChanged(nameof(IsWalkIn)); OnPropertyChanged(nameof(CustomerSearchQuery)); OnPropertyChanged(nameof(CustomerSearchResults)); OnPropertyChanged(nameof(SelectedSearchResult)); OnPropertyChanged(nameof(CustomerBills)); }
        public override void Dispose() { _timer.Stop(); base.Dispose(); }
    }
}
