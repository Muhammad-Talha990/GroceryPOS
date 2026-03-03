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
                if (_selectedTab != null) _selectedTab.IsActive = false;
                if (SetProperty(ref _selectedTab, value))
                {
                    if (_selectedTab != null) _selectedTab.IsActive = true;
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

        public Customer? SelectedCustomer { get => SelectedTab?.Customer; set { if (SelectedTab != null) { SelectedTab.Customer = value; SelectedTab.CustomerId = value?.CustomerId; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedCustomer)); } } }
        public bool HasSelectedCustomer => SelectedCustomer != null;

        private string _customerSearchQuery = string.Empty;
        public string CustomerSearchQuery { get => _customerSearchQuery; set { if (SetProperty(ref _customerSearchQuery, value)) SearchCustomers(); } }
        public ObservableCollection<Customer> CustomerSearchResults { get; set; } = new();
        public ObservableCollection<Bill> CustomerBills { get; set; } = new();
        public Customer? SelectedSearchResult { get; set; }
        private Bill? _selectedHistoryBill;
        public Bill? SelectedHistoryBill { get => _selectedHistoryBill; set { if (SetProperty(ref _selectedHistoryBill, value) && value != null) { LoadBillIntoCart(value); _selectedHistoryBill = null; OnPropertyChanged(); } } }

        public bool IsCustomerSearchFocused { get; set; }
        public bool IsRegistrationVisible { get; set; }
        public string NewCustomerName { get; set; } = "";
        public string NewCustomerPhone { get; set; } = "";
        public string NewCustomerAddress { get; set; } = "";
        public string RegistrationErrorMessage { get; set; } = "";

        public ObservableCollection<Item> ItemList { get; set; } = new();
        public Item? SelectedSearchItem { get; set; }
        public string BarcodeInput { get; set; } = "";
        public int QuantityInput { get; set; } = 1;

        public double SubTotal { get; set; }
        public double DiscountAmount { get; set; }
        public double TaxAmount { get; set; }
        public double GrandTotal { get; set; }
        public double ChangeAmount { get; set; }
        public bool IsChangeNegative => ChangeAmount < 0;
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
            ToggleRegistrationCommand = new RelayCommand(() => { IsRegistrationVisible = !IsRegistrationVisible; OnPropertyChanged(nameof(IsRegistrationVisible)); });
            SaveNewCustomerCommand = new RelayCommand(_ => SaveNewCustomer());
            NavigateSearchCommand = new RelayCommand(p => NavigateSearchResults(p?.ToString()));
            LoadProducts();
        }

        private void LoadProducts() { ItemList = new ObservableCollection<Item>(_itemService.GetAllItems()); }
        private void AddNewTab() { var tab = new BillingTab { TabName = $"Bill {Tabs.Count + 1}", InvoiceNumber = _billService.GetNextInvoiceNumber() }; Tabs.Add(tab); SelectedTab = tab; }
        private void CloseTab(BillingTab? tab) { if (tab == null || Tabs.Count <= 1) return; Tabs.Remove(tab); SelectedTab = Tabs.LastOrDefault(); for (int i = 0; i < Tabs.Count; i++) Tabs[i].TabName = $"Bill {i + 1}"; NotifyTabPropertiesChanged(); }
        private void ScanBarcode() { string bc = !string.IsNullOrWhiteSpace(BarcodeInput) ? BarcodeInput : SelectedSearchItem?.ItemId ?? ""; if (string.IsNullOrWhiteSpace(bc)) return; var it = _itemService.GetItemByBarcode(bc); if (it != null) { AddToCart(it); BarcodeInput = ""; SelectedSearchItem = null; OnPropertyChanged(nameof(BarcodeInput)); } else { StatusMessage = "✗ Item not found."; OnPropertyChanged(nameof(StatusMessage)); } }
        private void AddToCart(Item it) { if (SelectedTab == null) return; var ex = SelectedTab.CartItems.FirstOrDefault(i => i.ItemId == it.ItemId); if (ex != null) ex.Quantity += QuantityInput;            else SelectedTab.CartItems.Add(new CartItem { ItemId = it.ItemId, ItemDescription = it.Description, UnitPrice = it.SalePrice, Quantity = QuantityInput });
 QuantityInput = 1; RecalculateTotal(); }
        private void RemoveFromCart() { if (SelectedCartItem != null && SelectedTab != null) { SelectedTab.CartItems.Remove(SelectedCartItem); RecalculateTotal(); } }
        private void IncreaseQuantity() { if (SelectedCartItem != null) { SelectedCartItem.Quantity++; RecalculateTotal(); } }
        private void DecreaseQuantity() { if (SelectedCartItem != null && SelectedCartItem.Quantity > 1) { SelectedCartItem.Quantity--; RecalculateTotal(); } }
        private void RecalculateTotal() { if (SelectedTab == null) return; SubTotal = SelectedTab.CartItems.Sum(i => i.TotalPrice); double.TryParse(DiscountText, out var d); double.TryParse(TaxText, out var t); DiscountAmount = d; TaxAmount = t; GrandTotal = SubTotal - DiscountAmount + TaxAmount; CalculateChange(); OnPropertyChanged(nameof(SubTotal)); OnPropertyChanged(nameof(DiscountAmount)); OnPropertyChanged(nameof(TaxAmount)); OnPropertyChanged(nameof(GrandTotal)); OnPropertyChanged(nameof(CartItems)); }
        private void CalculateChange() { if (double.TryParse(CashReceivedText, out var c)) ChangeAmount = c - GrandTotal; else ChangeAmount = -GrandTotal; OnPropertyChanged(nameof(ChangeAmount)); OnPropertyChanged(nameof(IsChangeNegative)); }
        private async void CompleteSale() { try { if (SelectedTab == null || !SelectedTab.CartItems.Any()) { StatusMessage = "✗ Cart is empty."; OnPropertyChanged(nameof(StatusMessage)); return; } if (!double.TryParse(CashReceivedText, out var cr) || cr < GrandTotal) { StatusMessage = "✗ Insufficient cash."; OnPropertyChanged(nameof(StatusMessage)); return; } double.TryParse(DiscountText, out var d); double.TryParse(TaxText, out var t); var sb = _billService.CompleteBill(_authService.CurrentUser?.Id, SelectedCustomer?.CustomerId, SelectedTab.CartItems.Select(c => new Models.BillDescription { ItemId = c.ItemId, Quantity = c.Quantity, UnitPrice = c.UnitPrice, ItemDescription = c.ItemDescription }).ToList(), d, t, cr); _lastBill = sb; await AttemptPrint(sb); StatusMessage = $"✓ Sale Completed: Bill #{sb.InvoiceNumber}"; OnPropertyChanged(nameof(StatusMessage)); if (Tabs.Count > 1) CloseTab(SelectedTab); else ClearCart(); } catch (Exception ex) { StatusMessage = $"✗ Bill failed: {ex.Message}"; OnPropertyChanged(nameof(StatusMessage)); AppLogger.Error("Complete bill failed", ex); } }
        private async Task AttemptPrint(Bill b) { b.PrintAttempts++; if (!_printService.IsPrinterOnline()) { if (MessageBox.Show("Printer offline. Retry?", "Printer Error", MessageBoxButton.YesNo) == MessageBoxResult.Yes) await AttemptPrint(b); else _billRepo.UpdatePrintStatus(b.BillId, false, null, b.PrintAttempts); return; } if (_printService.PrintReceipt(b, _authService.CurrentUser?.FullName ?? "Cashier")) _billRepo.UpdatePrintStatus(b.BillId, true, DateTime.Now, b.PrintAttempts); else _billRepo.UpdatePrintStatus(b.BillId, false, null, b.PrintAttempts); }
        private void ClearCart() { if (SelectedTab == null) return; SelectedTab.CartItems.Clear(); SelectedTab.DiscountText = "0"; SelectedTab.TaxText = "0"; SelectedTab.CashReceivedText = "0"; ClearCustomer(); RecalculateTotal(); InvoiceNumber = _billService.GetNextInvoiceNumber(); }
        private void PrintLastReceipt() { if (_lastBill != null) _ = AttemptPrint(_lastBill); }
        private void SearchCustomers() { if (string.IsNullOrWhiteSpace(CustomerSearchQuery) || CustomerSearchQuery.Length < 3) { CustomerSearchResults.Clear(); return; } CustomerSearchResults = new ObservableCollection<Customer>(_customerService.SearchCustomers(CustomerSearchQuery)); OnPropertyChanged(nameof(CustomerSearchResults)); }
        private void SelectCustomer(Customer? c) { if (c == null) return; SelectedCustomer = c; CustomerSearchQuery = ""; CustomerSearchResults.Clear(); LoadCustomerHistory(c.CustomerId); }
        private void ClearCustomer() { SelectedCustomer = null; CustomerBills.Clear(); }
        private void LoadCustomerHistory(int id) { CustomerBills = new ObservableCollection<Bill>(_billRepo.GetBillsByCustomerId(id)); OnPropertyChanged(nameof(CustomerBills)); }
        private void RepeatLastOrder() { var lb = CustomerBills.FirstOrDefault(); if (lb != null) LoadBillIntoCart(lb); }
        private void LoadBillIntoCart(Bill b) { if (SelectedTab == null) return; SelectedTab.CartItems.Clear(); foreach (var it in b.Items) SelectedTab.CartItems.Add(new CartItem { ItemId = it.ItemId, ItemDescription = it.ItemDescription, UnitPrice = it.UnitPrice, Quantity = it.Quantity }); RecalculateTotal(); }
        private void SaveNewCustomer() { try { if (string.IsNullOrWhiteSpace(NewCustomerName) || string.IsNullOrWhiteSpace(NewCustomerPhone)) { RegistrationErrorMessage = "Name and Phone are required."; OnPropertyChanged(nameof(RegistrationErrorMessage)); return; } 
            var customer = new Customer { Name = NewCustomerName, PrimaryPhone = NewCustomerPhone, Address = NewCustomerAddress };
            _customerService.RegisterCustomer(customer);
            SelectCustomer(customer); 
            IsRegistrationVisible = false; OnPropertyChanged(nameof(IsRegistrationVisible)); NewCustomerName = NewCustomerPhone = NewCustomerAddress = ""; RegistrationErrorMessage = ""; }
            catch (Exception ex) { RegistrationErrorMessage = ex.Message; OnPropertyChanged(nameof(RegistrationErrorMessage)); } }
        private void NavigateSearchResults(string? d) { }
        private void NotifyTabPropertiesChanged() { OnPropertyChanged(nameof(CartItems)); OnPropertyChanged(nameof(DiscountText)); OnPropertyChanged(nameof(TaxText)); OnPropertyChanged(nameof(CashReceivedText)); OnPropertyChanged(nameof(InvoiceNumber)); OnPropertyChanged(nameof(SelectedCustomer)); }
        public override void Dispose() { _timer.Stop(); base.Dispose(); }
    }
}
