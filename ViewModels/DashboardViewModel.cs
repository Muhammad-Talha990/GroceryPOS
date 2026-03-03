using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using GroceryPOS.Models;
using GroceryPOS.Services;

namespace GroceryPOS.ViewModels
{
    /// <summary>
    /// ViewModel for the Dashboard screen.
    /// Shows today's summary statistics and recent bills.
    /// </summary>
    public class DashboardViewModel : BaseViewModel
    {
        private readonly ItemService _itemService;
        private readonly BillService _billService;
        private readonly IStockService _stockService;

        private double _todaySales;
        public double TodaySales { get => _todaySales; set => SetProperty(ref _todaySales, value); }

        private int _todaySaleCount;
        public int TodaySaleCount { get => _todaySaleCount; set => SetProperty(ref _todaySaleCount, value); }

        private int _totalProducts;
        public int TotalProducts { get => _totalProducts; set => SetProperty(ref _totalProducts, value); }

        private int _lowStockCount;
        public int LowStockCount { get => _lowStockCount; set => SetProperty(ref _lowStockCount, value); }

        private string _greeting = string.Empty;
        public string Greeting { get => _greeting; set => SetProperty(ref _greeting, value); }

        public ObservableCollection<Bill> RecentSales { get; set; } = new();
        public ObservableCollection<Item> LowStockItems { get; set; } = new();

        public ICommand RefreshCommand { get; }

        public DashboardViewModel(AuthService authService, ItemService itemService, BillService billService, IStockService stockService)
        {
            _itemService = itemService;
            _billService = billService;
            _stockService = stockService;

            var hour = DateTime.Now.Hour;
            var timeGreeting = hour < 12 ? "Good Morning" : hour < 17 ? "Good Afternoon" : "Good Evening";
            Greeting = $"{timeGreeting}, {authService.CurrentUser?.FullName ?? "User"}!";

            RefreshCommand = new RelayCommand(LoadData);

            // Real-time updates
            _stockService.StockChanged += LoadData;

            LoadData();
        }

        private void LoadData()
        {
            TodaySales = _billService.GetTodayTotal();
            TodaySaleCount = _billService.GetTodayBillCount();
            TotalProducts = _itemService.GetTotalItemCount();
            LowStockCount = _stockService.GetLowStockCount();

            Dispatch(() =>
            {
                RecentSales.Clear();
                foreach (var bill in _billService.GetTodayBills().Take(10))
                    RecentSales.Add(bill);

                LowStockItems.Clear();
                foreach (var item in _stockService.GetLowStockItems().Take(10))
                    LowStockItems.Add(item);
            });
        }
        public override void Dispose()
        {
            if (_stockService != null)
                _stockService.StockChanged -= LoadData;
            base.Dispose();
        }
    }
}
