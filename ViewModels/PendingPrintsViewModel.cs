using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using GroceryPOS.Models;
using GroceryPOS.Services;
using GroceryPOS.Data.Repositories;

namespace GroceryPOS.ViewModels
{
    public class PendingPrintsViewModel : BaseViewModel
    {
        private readonly BillRepository _billRepo;
        private readonly PrintService _printService;
        private readonly AuthService _authService;

        private ObservableCollection<Bill> _pendingBills = new();
        private Bill? _selectedBill;
        private string _statusMessage = "";
        private bool _isLoading;

        public ObservableCollection<Bill> PendingBills
        {
            get => _pendingBills;
            set => SetProperty(ref _pendingBills, value);
        }

        public Bill? SelectedBill
        {
            get => _selectedBill;
            set
            {
                if (SetProperty(ref _selectedBill, value))
                {
                    // Refresh command CanExecute state whenever selection changes
                    (PrintSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (MarkAsPrintedCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (CancelPrintCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public ICommand RefreshCommand { get; }
        public ICommand PrintSelectedCommand { get; }
        public ICommand MarkAsPrintedCommand { get; }
        public ICommand CancelPrintCommand { get; }

        public PendingPrintsViewModel(BillRepository billRepo, PrintService printService, AuthService authService)
        {
            _billRepo = billRepo;
            _printService = printService;
            _authService = authService;

            RefreshCommand = new RelayCommand(_ => LoadPendingBills());
            PrintSelectedCommand = new RelayCommand(_ => PrintSelected(), _ => SelectedBill != null);
            MarkAsPrintedCommand = new RelayCommand(_ => MarkAsPrinted(), _ => SelectedBill != null);
            CancelPrintCommand = new RelayCommand(_ => CancelPrint(), _ => SelectedBill != null);

            LoadPendingBills();
        }

        private void LoadPendingBills()
        {
            IsLoading = true;
            try
            {
                var bills = _billRepo.GetPendingPrintBills();
                PendingBills = new ObservableCollection<Bill>(bills);

                int count = bills.Count;
                StatusMessage = count > 0
                    ? $"{count} bill{(count == 1 ? "" : "s")} pending print."
                    : "All bills have been printed.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Error loading pending prints.";
                Helpers.AppLogger.Error("LoadPendingBills failed", ex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void PrintSelected()
        {
            if (SelectedBill == null) return;

            SelectedBill.PrintAttempts++;
            bool isOnline = _printService.IsPrinterOnline();

            if (!isOnline)
            {
                MessageBox.Show(
                    "Printer is offline or not detected. Please check the connection and try again.",
                    "Printer Offline", MessageBoxButton.OK, MessageBoxImage.Warning);
                _billRepo.UpdatePrintStatus(SelectedBill.BillId, false, null, SelectedBill.PrintAttempts);
                StatusMessage = $"✗ Printer offline — Bill #{SelectedBill.InvoiceNumber} not printed.";
                return;
            }

            bool success = _printService.PrintReceipt(SelectedBill, _authService.CurrentUser?.FullName ?? "Cashier");
            if (success)
            {
                DateTime now = DateTime.Now;
                _billRepo.UpdatePrintStatus(SelectedBill.BillId, true, now, SelectedBill.PrintAttempts);
                StatusMessage = $"✓ Receipt printed for Bill #{SelectedBill.InvoiceNumber}";
                PendingBills.Remove(SelectedBill);
                SelectedBill = null;
            }
            else
            {
                _billRepo.UpdatePrintStatus(SelectedBill.BillId, false, null, SelectedBill.PrintAttempts);
                StatusMessage = $"✗ Print failed for Bill #{SelectedBill.InvoiceNumber}. Check printer settings.";
                MessageBox.Show(
                    "Printing failed. Check that the printer is online and the spooler is running.",
                    "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MarkAsPrinted()
        {
            if (SelectedBill == null) return;

            var result = MessageBox.Show(
                $"Mark Bill #{SelectedBill.InvoiceNumber} as printed manually?\n\nUse this if the bill was printed via another method.",
                "Confirm Mark as Printed", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _billRepo.UpdatePrintStatus(SelectedBill.BillId, true, DateTime.Now, SelectedBill.PrintAttempts);
                StatusMessage = $"✓ Bill #{SelectedBill.InvoiceNumber} marked as printed.";
                PendingBills.Remove(SelectedBill);
                SelectedBill = null;
            }
        }

        private void CancelPrint()
        {
            if (SelectedBill == null) return;

            var result = MessageBox.Show(
                $"Cancel print job for Bill #{SelectedBill.InvoiceNumber}?\n\nThe bill record will remain in the database but be removed from this list.",
                "Confirm Cancel Print", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                // Mark as "printed" with no timestamp to hide it from the pending list
                // without deleting the underlying bill record.
                _billRepo.UpdatePrintStatus(SelectedBill.BillId, true, null, SelectedBill.PrintAttempts);
                StatusMessage = $"Print cancelled for Bill #{SelectedBill.InvoiceNumber}.";
                PendingBills.Remove(SelectedBill);
                SelectedBill = null;
            }
        }
    }
}
