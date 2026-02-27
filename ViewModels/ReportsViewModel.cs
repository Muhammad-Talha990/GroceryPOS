using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using GroceryPOS.Models;
using GroceryPOS.Services;
using GroceryPOS.Helpers;

namespace GroceryPOS.ViewModels
{
    /// <summary>
    /// ViewModel for the Reports screen.
    /// Supports Daily, Monthly, and Product-wise reports using the new Bill schema.
    /// </summary>
    public class ReportsViewModel : BaseViewModel
    {
        private readonly ReportService _reportService;

        public ObservableCollection<Bill> SalesReport { get; set; } = new();
        public ObservableCollection<ReportItem> ProductReport { get; set; } = new();

        private DateTime _fromDate = DateTime.Today;
        public DateTime FromDate
        {
            get => _fromDate;
            set { SetProperty(ref _fromDate, value); }
        }

        private DateTime _toDate = DateTime.Today.AddDays(1);
        public DateTime ToDate
        {
            get => _toDate;
            set { SetProperty(ref _toDate, value); }
        }

        private string _selectedReportType = "Daily";
        public string SelectedReportType
        {
            get => _selectedReportType;
            set 
            { 
                if (SetProperty(ref _selectedReportType, value))
                    GenerateReport();
            }
        }

        private double _totalRevenue;
        public double TotalRevenue { get => _totalRevenue; set => SetProperty(ref _totalRevenue, value); }

        private int _totalSalesCount;
        public int TotalSalesCount { get => _totalSalesCount; set => SetProperty(ref _totalSalesCount, value); }

        private bool _showSalesGrid = true;
        public bool ShowSalesGrid { get => _showSalesGrid; set => SetProperty(ref _showSalesGrid, value); }

        private bool _showProductGrid;
        public bool ShowProductGrid { get => _showProductGrid; set => SetProperty(ref _showProductGrid, value); }

        public ICommand GenerateReportCommand { get; }

        public ReportsViewModel(ReportService reportService)
        {
            _reportService = reportService;
            GenerateReportCommand = new RelayCommand(GenerateReport);
            GenerateReport();
        }

        public void GenerateReport()
        {
            try
            {
                AppLogger.Info($"Generating report: Type='{SelectedReportType}', From='{FromDate:yyyy-MM-dd}', To='{ToDate:yyyy-MM-dd}'");
                
                SalesReport.Clear();
                ProductReport.Clear();

                var type = SelectedReportType?.Trim();

                if (string.Equals(type, "Daily", StringComparison.OrdinalIgnoreCase))
                {
                    ShowSalesGrid = true;
                    ShowProductGrid = false;
                    var dailySales = _reportService.GetDailyReport(FromDate);
                    AppLogger.Info($"Daily report found {dailySales.Count} records.");
                    foreach (var s in dailySales)
                        SalesReport.Add(s);
                    TotalRevenue = dailySales.Sum(s => s.GrandTotal);
                    TotalSalesCount = dailySales.Count;
                }
                else if (string.Equals(type, "Weekly", StringComparison.OrdinalIgnoreCase))
                {
                    ShowSalesGrid = true;
                    ShowProductGrid = false;
                    var weeklySales = _reportService.GetWeeklyReport(FromDate);
                    AppLogger.Info($"Weekly report found {weeklySales.Count} records.");
                    foreach (var s in weeklySales)
                        SalesReport.Add(s);
                    TotalRevenue = weeklySales.Sum(s => s.GrandTotal);
                    TotalSalesCount = weeklySales.Count;
                }
                else if (string.Equals(type, "Monthly", StringComparison.OrdinalIgnoreCase))
                {
                    ShowSalesGrid = true;
                    ShowProductGrid = false;
                    var monthlySales = _reportService.GetMonthlyReport(FromDate.Year, FromDate.Month);
                    AppLogger.Info($"Monthly report found {monthlySales.Count} records.");
                    foreach (var s in monthlySales)
                        SalesReport.Add(s);
                    TotalRevenue = monthlySales.Sum(s => s.GrandTotal);
                    TotalSalesCount = monthlySales.Count;
                }
                else if (string.Equals(type, "Product-wise", StringComparison.OrdinalIgnoreCase))
                {
                    ShowSalesGrid = false;
                    ShowProductGrid = true;
                    var productReport = _reportService.GetProductWiseReport(FromDate, ToDate);
                    AppLogger.Info($"Product-wise report found {productReport.Count} items.");
                    foreach (var r in productReport)
                        ProductReport.Add(r);
                    TotalRevenue = productReport.Sum(r => r.TotalRevenue);
                    TotalSalesCount = productReport.Sum(r => r.QuantitySold);
                }
                else
                {
                    AppLogger.Warning($"Unknown report type received: '{type}'");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error generating report", ex);
            }
        }
    }
}
