using System;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using GroceryPOS.Helpers;
using GroceryPOS.Models;
using GroceryPOS.Services;

namespace GroceryPOS.ViewModels
{
    public class SupplierBillsViewModel : BaseViewModel
    {
        private readonly IStockService _stockService;
        private readonly IImageStorageService _imageService;

        public ObservableCollection<Stock> SupplyHistory { get; set; } = new();

        private Stock? _selectedEntry;
        public Stock? SelectedEntry
        {
            get => _selectedEntry;
            set
            {
                if (SetProperty(ref _selectedEntry, value))
                {
                    LoadImagePreview(value?.ImagePath);
                }
            }
        }

        private string _searchBarcode = string.Empty;
        public string SearchBarcode 
        { 
            get => _searchBarcode; 
            set 
            { 
                if (SetProperty(ref _searchBarcode, value))
                {
                    if (string.IsNullOrWhiteSpace(value))
                        _ = LoadAllRecentSupplies();
                    else if (value.Length >= 2)
                        _ = ExecuteSearchItem();
                }
            } 
        }

        private Item? _foundItem;
        public Item? FoundItem
        {
            get => _foundItem;
            set
            {
                if (SetProperty(ref _foundItem, value))
                {
                    (SaveSupplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private BitmapImage? _billImagePreview;
        public BitmapImage? BillImagePreview
        {
            get => _billImagePreview;
            set => SetProperty(ref _billImagePreview, value);
        }

        private bool _isImageAvailable;
        public bool IsImageAvailable
        {
            get => _isImageAvailable;
            set => SetProperty(ref _isImageAvailable, value);
        }

        // --- Registration Form Fields ---
        private int _formQuantity;
        public int FormQuantity 
        { 
            get => _formQuantity; 
            set
            {
                if (SetProperty(ref _formQuantity, value))
                {
                    (SaveSupplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private string _tempImagePath = string.Empty;
        public string TempImagePath { get => _tempImagePath; set => SetProperty(ref _tempImagePath, value); }

        private bool _isAddingNew;
        public bool IsAddingNew { get => _isAddingNew; set => SetProperty(ref _isAddingNew, value); }

        private string _manualSearchBarcode = string.Empty;
        public string ManualSearchBarcode { get => _manualSearchBarcode; set => SetProperty(ref _manualSearchBarcode, value); }

        private DateTime _currentSystemDate;
        public DateTime CurrentSystemDate { get => _currentSystemDate; set => SetProperty(ref _currentSystemDate, value); }

        private string _statusMessage = string.Empty;
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        public ICommand SearchItemCommand { get; }
        public ICommand ShowAddFormCommand { get; }
        public ICommand SelectImageCommand { get; }
        public ICommand SaveSupplyCommand { get; }
        public ICommand CancelAddCommand { get; }
        public ICommand DeleteSupplyCommand { get; }
        public ICommand FindManualItemCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ViewFullBillCommand { get; }

        public SupplierBillsViewModel(IStockService stockService, IImageStorageService imageService)
        {
            _stockService = stockService;
            _imageService = imageService;

            SearchItemCommand = new RelayCommand(async () => await ExecuteSearchItem());
            ShowAddFormCommand = new RelayCommand(ExecuteShowAddForm);
            SelectImageCommand = new RelayCommand(ExecuteSelectImage);
            SaveSupplyCommand = new RelayCommand(async () => await ExecuteSaveSupply(), () => FoundItem != null && FormQuantity > 0);
            CancelAddCommand = new RelayCommand(() => IsAddingNew = false);
            DeleteSupplyCommand = new RelayCommand(async () => await ExecuteDeleteSupply(), () => SelectedEntry != null);
            FindManualItemCommand = new RelayCommand(async () => await ExecuteFindManualItem());
            RefreshCommand = new RelayCommand(async () => await LoadAllRecentSupplies());
            ViewFullBillCommand = new RelayCommand(ExecuteViewFullBill, () => SelectedEntry != null && !string.IsNullOrEmpty(SelectedEntry.ImagePath));

            // Subscribe to real-time stock changes
            _stockService.StockChanged += OnStockChanged;

            // Auto-load recent supplies on startup
            _ = LoadAllRecentSupplies();
        }

        private async void OnStockChanged()
        {
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (FoundItem != null)
                        await LoadSupplyHistory();
                    else
                        await LoadAllRecentSupplies();
                });
            }
            catch (Exception ex)
            {
                AppLogger.Error("StockChanged handler failed", ex);
            }
        }

        private async Task LoadAllRecentSupplies()
        {
            try
            {
                StatusMessage = "Loading recent supplies...";
                var allSupplies = await _stockService.GetAllRecentSuppliesAsync(50);
                SupplyHistory.Clear();
                foreach (var entry in allSupplies) SupplyHistory.Add(entry);
                StatusMessage = $"Showing {allSupplies.Count} recent supply entries.";
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load all supplies", ex);
                StatusMessage = "Error loading supplies.";
            }
        }

        private void ExecuteShowAddForm()
        {
            AppLogger.Info("ExecuteShowAddForm called.");
            
            // Even if FoundItem is null, let's open it to show the user the form exists, 
            // but we'll handle the "no item" state in the UI.
            IsAddingNew = true;
            
            // Reset form
            FormQuantity = 0;
            TempImagePath = string.Empty;
            ManualSearchBarcode = string.Empty;
            CurrentSystemDate = DateTime.Now;
            
            if (FoundItem == null)
            {
                StatusMessage = "Please search for an item first to record a supply entry.";
            }
            else
            {
                StatusMessage = $"Registering supply for: {FoundItem.Description}";
            }
        }

        private async Task ExecuteSearchItem()
        {
            try
            {
                StatusMessage = "Searching...";
                // We'll use a simple DB query via DatabaseHelper since we don't have a direct "GetItem" in StockService
                // But better to use the repository if possible. For now, let's assume we can fetch it.
                // In a real app, I'd inject ItemRepository here too.
                using var conn = Data.DatabaseHelper.GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM Item WHERE itemId = @bid OR Description LIKE @name LIMIT 1;";
                cmd.Parameters.AddWithValue("@bid", SearchBarcode);
                cmd.Parameters.AddWithValue("@name", $"%{SearchBarcode}%");

                using var reader = await cmd.ExecuteReaderAsync();
                if (reader.Read())
                {
                    FoundItem = new Item
                    {
                        ItemId = reader.GetString(reader.GetOrdinal("itemId")),
                        Description = reader.GetString(reader.GetOrdinal("Description")),
                        StockQuantity = reader.GetDouble(reader.GetOrdinal("StockQuantity"))
                    };
                    await LoadSupplyHistory();
                }
                else
                {
                    FoundItem = null;
                    SupplyHistory.Clear();
                    StatusMessage = "Item not found.";
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Search failed", ex);
                StatusMessage = "Search error.";
            }
        }

        private async Task ExecuteFindManualItem()
        {
            if (string.IsNullOrWhiteSpace(ManualSearchBarcode)) return;
            
            try
            {
                StatusMessage = "Looking for item...";
                using var conn = Data.DatabaseHelper.GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM Item WHERE itemId = @bid OR Description LIKE @name LIMIT 1;";
                cmd.Parameters.AddWithValue("@bid", ManualSearchBarcode);
                cmd.Parameters.AddWithValue("@name", $"%{ManualSearchBarcode}%");

                using var reader = await cmd.ExecuteReaderAsync();
                if (reader.Read())
                {
                    FoundItem = new Item
                    {
                        ItemId = reader.GetString(reader.GetOrdinal("itemId")),
                        Description = reader.GetString(reader.GetOrdinal("Description")),
                        StockQuantity = reader.GetDouble(reader.GetOrdinal("StockQuantity"))
                    };
                    StatusMessage = "Item confirmed: " + FoundItem.Description;
                    await LoadSupplyHistory();
                }
                else
                {
                    StatusMessage = "Item not found. Please try another barcode.";
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Manual search failed", ex);
                StatusMessage = "Error finding item.";
            }
        }

        private async Task LoadSupplyHistory()
        {
            if (FoundItem == null) return;
            try
            {
                var history = await _stockService.GetSupplyHistoryAsync(FoundItem.ItemId);
                SupplyHistory.Clear();
                foreach (var entry in history) SupplyHistory.Add(entry);
                StatusMessage = $"Found {history.Count} supply entries.";
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load supply history", ex);
                StatusMessage = "Error loading history.";
            }
        }

        private void ExecuteSelectImage()
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image files (*.jpg, *.jpeg, *.png)|*.jpg;*.jpeg;*.png",
                Title = "Select Supplier Bill Image"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TempImagePath = openFileDialog.FileName;
                StatusMessage = "Image selected: " + Path.GetFileName(TempImagePath);
            }
        }

        private async Task ExecuteSaveSupply()
        {
            if (FoundItem == null) return;
            try
            {
                var entry = new Stock
                {
                    ProductId = FoundItem.ItemId,
                    Quantity = FormQuantity
                };

                await _stockService.RegisterSupplyAsync(entry, TempImagePath);
                
                IsAddingNew = false;
                await ExecuteSearchItem(); // Refresh item data and history
                StatusMessage = "Supply registered successfully!";
                ClearForm();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to save supply", ex);
                MessageBox.Show("Error registration supply: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ExecuteDeleteSupply()
        {
            if (SelectedEntry == null) return;

            var result = MessageBox.Show($"Are you sure you want to delete this supply entry of {SelectedEntry.Quantity} units?\n\nThis will also revert the stock quantity of the item.", 
                                       "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
            if (result != MessageBoxResult.Yes) return;

            try
            {
                StatusMessage = "Deleting...";
                await _stockService.DeleteSupplyAsync(SelectedEntry.Id);
                await ExecuteSearchItem(); // Refresh
                StatusMessage = "Supply deleted successfully.";
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to delete supply", ex);
                MessageBox.Show("Error deleting supply: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExecuteViewFullBill()
        {
            if (SelectedEntry == null || string.IsNullOrEmpty(SelectedEntry.ImagePath)) return;
            
            try
            {
                if (File.Exists(SelectedEntry.ImagePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = SelectedEntry.ImagePath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show("Bill image file not found on disk.", "File Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to open bill image", ex);
                MessageBox.Show("Error opening bill image: " + ex.Message);
            }
        }

        private void ClearForm()
        {
            FormQuantity = 0;
            TempImagePath = string.Empty;
        }

        private void LoadImagePreview(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                BillImagePreview = null;
                IsImageAvailable = false;
                if (!string.IsNullOrEmpty(path)) StatusMessage = "Image file missing.";
                return;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                
                BillImagePreview = bitmap;
                IsImageAvailable = true;
                StatusMessage = "Previewing supply bill image.";
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Error loading preview for {path}", ex);
                BillImagePreview = null;
                IsImageAvailable = false;
                StatusMessage = "Error loading image preview.";
            }
        }
    }
}
