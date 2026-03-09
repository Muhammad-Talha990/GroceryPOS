using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using GroceryPOS.Helpers;
using GroceryPOS.Models;
using GroceryPOS.Services;

namespace GroceryPOS.ViewModels
{
    /// <summary>
    /// ViewModel for the Customer Ledger screen.
    /// Shows all bills for a customer with credit summary and allows recording payments.
    /// </summary>
    public class CustomerLedgerViewModel : BaseViewModel
    {
        private readonly CreditService _creditService;
        private readonly CustomerService _customerService;
        private readonly PrintService _printService;
        private readonly AuthService _authService;
        private readonly IStockService _stockService;
        private readonly IReturnService _returnService;

        // ── Selected customer ──
        private Customer? _customer;
        public Customer? Customer
        {
            get => _customer;
            private set => SetProperty(ref _customer, value);
        }

        // ── Ledger data ──
        public ObservableCollection<Bill> LedgerEntries { get; } = new();

        // ── Summary footer ──
        private double _totalCredit;
        public double TotalCredit
        {
            get => _totalCredit;
            private set => SetProperty(ref _totalCredit, value);
        }

        private double _totalPaid;
        public double TotalPaid
        {
            get => _totalPaid;
            private set => SetProperty(ref _totalPaid, value);
        }

        private double _totalPending;
        public double TotalPending
        {
            get => _totalPending;
            private set => SetProperty(ref _totalPending, value);
        }

        // ── Record Payment panel ──
        private bool _isPaymentPanelOpen;
        public bool IsPaymentPanelOpen
        {
            get => _isPaymentPanelOpen;
            set => SetProperty(ref _isPaymentPanelOpen, value);
        }

        private Bill? _selectedBill;
        public Bill? SelectedBill
        {
            get => _selectedBill;
            set
            {
                if (SetProperty(ref _selectedBill, value))
                {
                    OnPropertyChanged(nameof(HasSelectedBill));
                    OnPropertyChanged(nameof(SelectedBillRemaining));
                    LoadPaymentHistory();
                }
            }
        }
        public bool HasSelectedBill => SelectedBill != null;
        public double SelectedBillRemaining => SelectedBill?.RemainingAmount ?? 0;


        private bool _isBillDetailOpen;
        public bool IsBillDetailOpen
        {
            get => _isBillDetailOpen;
            set => SetProperty(ref _isBillDetailOpen, value);
        }

        public ObservableCollection<CreditPayment> PaymentHistory { get; } = new();

        private void LoadPaymentHistory()
        {
            PaymentHistory.Clear();
            if (SelectedBill == null) return;

            try
            {
                var history = _creditService.GetPaymentHistory(SelectedBill.BillId);
                
                double subsequentPaymentsTotal = history.Sum(h => h.AmountPaid);
                double initialPayment = Math.Round(SelectedBill.PaidAmount - subsequentPaymentsTotal, 2);

                foreach (var p in history)
                {
                    PaymentHistory.Add(p);
                }

                if (initialPayment > 0)
                {
                    PaymentHistory.Add(new CreditPayment
                    {
                        BillId = SelectedBill.BillId,
                        AmountPaid = initialPayment,
                        PaidAt = SelectedBill.BillDateTime,
                        Note = "Initial Payment (At checkout)"
                    });
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("CustomerLedgerViewModel.LoadPaymentHistory failed", ex);
            }
        }

        private string _paymentAmountText = string.Empty;
        public string PaymentAmountText
        {
            get => _paymentAmountText;
            set => SetProperty(ref _paymentAmountText, value);
        }

        private string _paymentNote = string.Empty;
        public string PaymentNote
        {
            get => _paymentNote;
            set => SetProperty(ref _paymentNote, value);
        }

        private string _paymentError = string.Empty;
        public string PaymentError
        {
            get => _paymentError;
            set => SetProperty(ref _paymentError, value);
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // ── Commands ──
        public ICommand RefreshCommand { get; }
        public ICommand OpenPaymentPanelCommand { get; }
        public ICommand ClosePaymentPanelCommand { get; }
        public ICommand RecordPaymentCommand { get; }
        public ICommand PayFullRemainingCommand { get; }
        public ICommand ViewBillCommand { get; }
        public ICommand PrintBillCommand { get; }
        public ICommand CloseBillDetailCommand { get; }
        public ICommand CloseSidebarCommand { get; }

        /// <summary>Raised when the user wants to go back to Customer Management.</summary>
        public event Action? GoBackRequested;
        public ICommand GoBackCommand { get; }

        public CustomerLedgerViewModel(CreditService creditService, CustomerService customerService, PrintService printService, AuthService authService, IStockService stockService, IReturnService returnService)
        {
            _creditService  = creditService;
            _customerService = customerService;
            _printService = printService;
            _authService = authService;
            _stockService = stockService;
            _returnService = returnService;

            // Real-time refresh whenever stock/billing events occur
            _stockService.StockChanged += OnDataChanged;

            RefreshCommand          = new RelayCommand(_ => Refresh());
            OpenPaymentPanelCommand = new RelayCommand(obj => OpenPaymentPanel(obj as Bill));
            ClosePaymentPanelCommand= new RelayCommand(_ => ClosePaymentPanel());
            RecordPaymentCommand    = new RelayCommand(_ => RecordPayment());
            PayFullRemainingCommand = new RelayCommand(_ => PayFullRemaining());
            ViewBillCommand         = new RelayCommand(obj => ViewBill(obj as Bill));
            PrintBillCommand        = new RelayCommand(obj => PrintBill(obj as Bill));
            CloseBillDetailCommand  = new RelayCommand(_ => CloseBillDetail());
            CloseSidebarCommand     = new RelayCommand(_ => CloseSidebar());
            GoBackCommand           = new RelayCommand(_ => GoBackRequested?.Invoke());
        }

        // ────────────────────────────────────────────
        //  LOAD
        // ────────────────────────────────────────────

        public void Load(int customerId)
        {
            try
            {
                // Reset UI state from any previous customer
                SelectedBill = null;
                IsPaymentPanelOpen = false;
                IsBillDetailOpen = false;
                StatusMessage = string.Empty;

                Customer = _customerService.GetCustomerById(customerId);
                if (Customer == null)
                {
                    StatusMessage = "Customer not found.";
                    return;
                }
                LoadLedger();
            }
            catch (Exception ex)
            {
                AppLogger.Error("CustomerLedgerViewModel.Load failed", ex);
                StatusMessage = "⚠ Failed to load ledger.";
            }
        }

        private void LoadLedger()
        {
            if (Customer == null) return;

            try
            {
                var entries = _creditService.GetLedger(Customer.CustomerId);
                LedgerEntries.Clear();
                foreach (var b in entries)
                    LedgerEntries.Add(b);

                var (credit, paid, pending) = _creditService.GetPendingSummary(Customer.CustomerId);
                TotalCredit  = credit;
                TotalPaid    = paid;
                TotalPending = pending;

                // Refresh the customer's pending credit too
                Customer.PendingCredit = pending;
                OnPropertyChanged(nameof(Customer));
            }
            catch (Exception ex)
            {
                AppLogger.Error("CustomerLedgerViewModel.LoadLedger failed", ex);
                StatusMessage = "⚠ Failed to refresh ledger.";
            }
        }

        private void Refresh()
        {
            ClosePaymentPanel();
            LoadLedger();
            StatusMessage = string.Empty;
        }

        // ────────────────────────────────────────────
        //  PAYMENT RECORDING
        // ────────────────────────────────────────────

        private void OpenPaymentPanel(Bill? bill)
        {
            if (bill == null || !bill.HasPendingCredit) return;
            SelectedBill       = bill;
            PaymentAmountText  = string.Empty;
            PaymentNote        = string.Empty;
            PaymentError       = string.Empty;
            IsPaymentPanelOpen = true;
        }

        private void ClosePaymentPanel()
        {
            IsPaymentPanelOpen = false;
            SelectedBill       = null;
            PaymentAmountText  = string.Empty;
            PaymentError       = string.Empty;
        }

        private void PayFullRemaining()
        {
            if (SelectedBill != null)
                PaymentAmountText = SelectedBill.RemainingAmount.ToString("F2");
        }

        private void RecordPayment()
        {
            try
            {
                PaymentError = string.Empty;

                if (SelectedBill == null)
                {
                    PaymentError = "No bill selected.";
                    return;
                }

                if (!double.TryParse(PaymentAmountText, out double amount) || amount <= 0)
                {
                    PaymentError = "Please enter a valid amount greater than zero.";
                    return;
                }

                // Capture details before the service call, as it triggers a refresh that nulls SelectedBill
                string invoiceNumber = SelectedBill.InvoiceNumber;

                _creditService.RecordPayment(SelectedBill.BillId, amount, PaymentNote);

                StatusMessage = $"✓ Payment of Rs. {amount:N2} recorded successfully for Bill #{invoiceNumber}.";
                MessageBox.Show(StatusMessage, "Payment Recorded", MessageBoxButton.OK, MessageBoxImage.Information);
                OnPropertyChanged(nameof(StatusMessage));

                ClosePaymentPanel();
                LoadLedger();
            }
            catch (Exception ex)
            {
                PaymentError = ex.Message;
                AppLogger.Error("CustomerLedgerViewModel.RecordPayment failed", ex);
            }
        }
        private async void ViewBill(Bill? bill)
        {
            if (bill == null) return;

            if (bill.IsReturn && bill.ParentBillId.HasValue)
            {
                try 
                {
                    var (original, returns) = await _returnService.GetBillWithReturnHistory(bill.ParentBillId.Value);
                    bill.ParentBill = original;
                    bill.ReturnHistory = returns.Where(r => r.BillId != bill.BillId).ToList();
                    
                    double previousReturnsTotal = returns.Where(r => r.BillId < bill.BillId).Sum(r => Math.Abs(r.GrandTotal));
                    bill.RemainingDueAfterThisReturn = original.GrandTotal - previousReturnsTotal - Math.Abs(bill.GrandTotal);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Failed to fetch return metadata for view (Ledger)", ex);
                }
            }

            SelectedBill = bill;
            IsBillDetailOpen = true;
        }

        private async void PrintBill(Bill? bill)
        {
            if (bill == null) return;

            if (bill.IsReturn && bill.ParentBillId.HasValue && bill.ParentBill == null)
            {
                try 
                {
                    var (original, returns) = await _returnService.GetBillWithReturnHistory(bill.ParentBillId.Value);
                    bill.ParentBill = original;
                    bill.ReturnHistory = returns.Where(r => r.BillId != bill.BillId).ToList();

                    double previousReturnsTotal = returns.Where(r => r.BillId < bill.BillId).Sum(r => Math.Abs(r.GrandTotal));
                    bill.RemainingDueAfterThisReturn = original.GrandTotal - previousReturnsTotal - Math.Abs(bill.GrandTotal);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Failed to fetch return metadata for print (Ledger)", ex);
                }
            }
            
            bool isOnline = _printService.IsPrinterOnline();

            if (isOnline)
            {
                bool printSuccess = _printService.PrintReceipt(bill, _authService.CurrentUser?.FullName ?? "System Admin");
                if (!printSuccess)
                {
                    System.Windows.MessageBox.Show("Failed to communicate with the printer. Please check the connection.", "Print Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
            else
            {
                System.Windows.MessageBox.Show("Printer is currently unavailable or offline.\nPlease ensure the printer is connected and turned on.", "Printer Offline", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private void CloseBillDetail()
        {
            IsBillDetailOpen = false;
        }

        private void CloseSidebar()
        {
            SelectedBill = null;
        }

        private void OnDataChanged()
        {
            if (Customer != null)
                Dispatch(() => LoadLedger());
        }

        public override void Dispose()
        {
            if (_stockService != null)
                _stockService.StockChanged -= OnDataChanged;
            base.Dispose();
        }
    }
}
