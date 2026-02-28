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
            set 
            { 
                if (SetProperty(ref _fromDate, value))
                    GenerateReport();
            }
        }

        private DateTime _toDate = DateTime.Today;
        public DateTime ToDate
        {
            get => _toDate;
            set 
            { 
                if (SetProperty(ref _toDate, value))
                    GenerateReport();
            }
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
        public ICommand ExportReportCommand { get; }

        public ReportsViewModel(ReportService reportService)
        {
            _reportService = reportService;
            GenerateReportCommand = new RelayCommand(GenerateReport);
            ExportReportCommand = new RelayCommand(ExportReport);
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
                    // Daily shortcut: just use FromDate for that day
                    var start = FromDate.Date;
                    var end = start.AddDays(1);
                    var data = _reportService.GetByDateRange(start, end);
                    foreach (var s in data) SalesReport.Add(s);
                    TotalRevenue = data.Sum(s => s.GrandTotal);
                    TotalSalesCount = data.Count;
                }
                else if (string.Equals(type, "Weekly", StringComparison.OrdinalIgnoreCase))
                {
                    ShowSalesGrid = true;
                    ShowProductGrid = false;
                    // Weekly shortcut: 7 days from FromDate
                    var start = FromDate.Date;
                    var end = start.AddDays(7);
                    var data = _reportService.GetByDateRange(start, end);
                    foreach (var s in data) SalesReport.Add(s);
                    TotalRevenue = data.Sum(s => s.GrandTotal);
                    TotalSalesCount = data.Count;
                }
                else if (string.Equals(type, "Monthly", StringComparison.OrdinalIgnoreCase))
                {
                    ShowSalesGrid = true;
                    ShowProductGrid = false;
                    // Monthly shortcut: the whole month of FromDate
                    var start = new DateTime(FromDate.Year, FromDate.Month, 1);
                    var end = start.AddMonths(1);
                    var data = _reportService.GetByDateRange(start, end);
                    foreach (var s in data) SalesReport.Add(s);
                    TotalRevenue = data.Sum(s => s.GrandTotal);
                    TotalSalesCount = data.Count;
                }
                else if (string.Equals(type, "Custom Range", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(type))
                {
                    ShowSalesGrid = true;
                    ShowProductGrid = false;
                    var start = FromDate.Date;
                    var end = ToDate.Date.AddDays(1);
                    var data = _reportService.GetByDateRange(start, end);
                    foreach (var s in data) SalesReport.Add(s);
                    TotalRevenue = data.Sum(s => s.GrandTotal);
                    TotalSalesCount = data.Count;
                }
                else if (string.Equals(type, "Product-wise", StringComparison.OrdinalIgnoreCase))
                {
                    ShowSalesGrid = false;
                    ShowProductGrid = true;
                    var start = FromDate.Date;
                    var end = ToDate.Date.AddDays(1);
                    var data = _reportService.GetProductWiseReport(start, end);
                    foreach (var r in data) ProductReport.Add(r);
                    TotalRevenue = data.Sum(r => r.TotalRevenue);
                    TotalSalesCount = data.Sum(r => r.QuantitySold);
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
        private void ExportReport()
        {
            try
            {
                if (ShowSalesGrid && SalesReport.Count == 0) return;
                if (ShowProductGrid && ProductReport.Count == 0) return;

                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv",
                    FileName = $"Report_{SelectedReportType}_{DateTime.Now:yyyyMMdd_HHmm}.csv"
                };

                if (sfd.ShowDialog() == true)
                {
                    var csv = new System.Text.StringBuilder();

                    if (ShowSalesGrid)
                    {
                        csv.AppendLine("Invoice #,Cashier,Sub Total,Discount,Tax,Grand Total,Date/Time");
                        foreach (var b in SalesReport)
                        {
                            csv.AppendLine($"{b.InvoiceNumber},{b.User?.FullName ?? "Unknown"},{b.SubTotal},{b.DiscountAmount},{b.TaxAmount},{b.GrandTotal},{b.SaleDate:yyyy-MM-dd HH:mm}");
                        }
                    }
                    else
                    {
                        csv.AppendLine("Product Name,Quantity Sold,Total Revenue");
                        foreach (var p in ProductReport)
                        {
                            csv.AppendLine($"\"{p.ItemDescription}\",{p.QuantitySold},{p.TotalRevenue}");
                        }
                    }

                    System.IO.File.WriteAllText(sfd.FileName, csv.ToString());
                    AppLogger.Info($"Report exported to {sfd.FileName}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error exporting report", ex);
            }
        }
    }
}
