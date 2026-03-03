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
            set => SetProperty(ref _selectedBill, value);
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
                StatusMessage = bills.Any() ? $"{bills.Count} pending prints found." : "No pending prints.";
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
                MessageBox.Show("Printer is still offline. Please check connection and try again.", 
                    "Printer Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                _billRepo.UpdatePrintStatus(SelectedBill.BillId, false, null, SelectedBill.PrintAttempts);
                return;
            }

            bool success = _printService.PrintReceipt(SelectedBill, _authService.CurrentUser?.FullName ?? "Cashier");
            if (success)
            {
                DateTime now = DateTime.Now;
                _billRepo.UpdatePrintStatus(SelectedBill.BillId, true, now, SelectedBill.PrintAttempts);
                StatusMessage = $"✓ Receipt printed for Bill #{SelectedBill.InvoiceNumber}";
                PendingBills.Remove(SelectedBill);
            }
            else
            {
                _billRepo.UpdatePrintStatus(SelectedBill.BillId, false, null, SelectedBill.PrintAttempts);
                MessageBox.Show("Printing failed again. Check printer or spooler settings.", 
                    "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MarkAsPrinted()
        {
            if (SelectedBill == null) return;

            var result = MessageBox.Show("Mark this bill as printed manually?", "Confirm", MessageBoxButton.YesNo);
            if (result == MessageBoxResult.Yes)
            {
                _billRepo.UpdatePrintStatus(SelectedBill.BillId, true, DateTime.Now, SelectedBill.PrintAttempts);
                PendingBills.Remove(SelectedBill);
                StatusMessage = "Bill marked as printed.";
            }
        }

        private void CancelPrint()
        {
            if (SelectedBill == null) return;

            var result = MessageBox.Show("Are you sure you want to cancel printing for this bill? (It will be removed from this list but not deleted from database)", 
                "Confirm Cancel", MessageBoxButton.YesNo);
            
            if (result == MessageBoxResult.Yes)
            {
                // We mark it as 'Printed' in DB just to hide it from this list, 
                // but without a timestamp strictly speaking it might be ambiguous.
                // However the requirement is "Allow cancel print".
                _billRepo.UpdatePrintStatus(SelectedBill.BillId, true, null, SelectedBill.PrintAttempts);
                PendingBills.Remove(SelectedBill);
                StatusMessage = "Print cancelled.";
            }
        }
    }
}
