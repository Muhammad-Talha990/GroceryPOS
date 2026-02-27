using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using GroceryPOS.ViewModels;

namespace GroceryPOS.Models
{
    public class BillingTab : BaseViewModel
    {
        private string _tabName = string.Empty;
        public string TabName
        {
            get => _tabName;
            set => SetProperty(ref _tabName, value);
        }

        private string _invoiceNumber = "00000";
        public string InvoiceNumber
        {
            get => _invoiceNumber;
            set => SetProperty(ref _invoiceNumber, value);
        }

        public ObservableCollection<CartItem> CartItems { get; set; } = new();

        private string _discountText = "0";
        public string DiscountText
        {
            get => _discountText;
            set => SetProperty(ref _discountText, value);
        }

        private string _taxText = "0";
        public string TaxText
        {
            get => _taxText;
            set => SetProperty(ref _taxText, value);
        }

        private string _cashReceivedText = "0";
        public string CashReceivedText
        {
            get => _cashReceivedText;
            set => SetProperty(ref _cashReceivedText, value);
        }

        // Helper to check if this tab is the active one (managed by ViewModel)
        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }
    }
}
