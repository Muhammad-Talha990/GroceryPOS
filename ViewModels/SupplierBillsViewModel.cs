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
        public ObservableCollection<Item> ProductSearchResults { get; set; } = new();
        public ObservableCollection<Item> MainProductSearchResults { get; set; } = new();

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
                    {
                        IsMainResultsOpen = false;
                        _ = LoadAllRecentSupplies();
                    }
                    else
                    {
                        _ = ExecuteLiveMainSearch();
                    }
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

        private BitmapImage? _formImagePreview;
        public BitmapImage? FormImagePreview
        {
            get => _formImagePreview;
            set
            {
                if (SetProperty(ref _formImagePreview, value))
                {
                    (SaveSupplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        // --- Registration Form Fields ---
        private int _formQuantity;
        public int FormQuantity 
        { 
            get => _formQuantity; 
            set
            {
                int validatedValue = value < 1 ? 1 : value;
                if (SetProperty(ref _formQuantity, validatedValue))
                {
                    (SaveSupplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private string _tempImagePath = string.Empty;
        public string TempImagePath { get => _tempImagePath; set => SetProperty(ref _tempImagePath, value); }

        private bool _isAddingNew;
        public bool IsAddingNew { get => _isAddingNew; set => SetProperty(ref _isAddingNew, value); }

        private bool _isEditing;
        public bool IsEditing { get => _isEditing; set => SetProperty(ref _isEditing, value); }

        private bool _isSearchResultsOpen;
        public bool IsSearchResultsOpen { get => _isSearchResultsOpen; set => SetProperty(ref _isSearchResultsOpen, value); }

        private bool _isMainResultsOpen;
        public bool IsMainResultsOpen { get => _isMainResultsOpen; set => SetProperty(ref _isMainResultsOpen, value); }

        private Item? _selectedProductResult;
        public Item? SelectedProductResult
        {
            get => _selectedProductResult;
            set
            {
                if (SetProperty(ref _selectedProductResult, value) && value != null)
                {
                    ExecuteSelectProduct(value);
                }
            }
        }

        private Item? _selectedMainProductResult;
        public Item? SelectedMainProductResult
        {
            get => _selectedMainProductResult;
            set
            {
                if (SetProperty(ref _selectedMainProductResult, value) && value != null)
                {
                    ExecuteSelectMainProduct(value);
                }
            }
        }

        private string _manualSearchBarcode = string.Empty;
        public string ManualSearchBarcode 
        { 
            get => _manualSearchBarcode; 
            set 
            { 
                if (SetProperty(ref _manualSearchBarcode, value))
                {
                    if (string.IsNullOrWhiteSpace(value))
                        IsSearchResultsOpen = false;
                    else
                        _ = ExecuteFindManualItem();
                }
            } 
        }

        private DateTime _currentSystemDate;
        public DateTime CurrentSystemDate { get => _currentSystemDate; set => SetProperty(ref _currentSystemDate, value); }

        private string _statusMessage = string.Empty;
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        public ICommand SearchItemCommand { get; }
        public ICommand ShowAddFormCommand { get; }
        public ICommand SelectImageCommand { get; }
        public ICommand SaveSupplyCommand { get; }
        public ICommand CancelAddCommand { get; }
        public ICommand UpdateSupplyCommand { get; }
        public ICommand FindManualItemCommand { get; }
        public ICommand SelectProductCommand { get; }
        public ICommand SelectMainProductCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ViewFullBillCommand { get; }
        public ICommand ToggleSearchResultsCommand { get; }

        public SupplierBillsViewModel(IStockService stockService, IImageStorageService imageService)
        {
            _stockService = stockService;
            _imageService = imageService;

            SearchItemCommand = new RelayCommand(async () => await ExecuteExplicitSearchItem());
            ShowAddFormCommand = new RelayCommand(ExecuteShowAddForm);
            SelectImageCommand = new RelayCommand(ExecuteSelectImage);
            SaveSupplyCommand = new RelayCommand(async () => await ExecuteSaveSupply(), () => FoundItem != null && FormQuantity > 0 && FormImagePreview != null);
            CancelAddCommand = new RelayCommand(() => IsAddingNew = false);
            UpdateSupplyCommand = new RelayCommand(async (s) => await ExecuteUpdateSupply(s as Stock));
            FindManualItemCommand = new RelayCommand(async () => await ExecuteExplicitManualSearch());
            SelectProductCommand = new RelayCommand((p) => ExecuteSelectProduct(p as Item));
            SelectMainProductCommand = new RelayCommand((p) => ExecuteSelectMainProduct(p as Item));
            RefreshCommand = new RelayCommand(async () => await LoadAllRecentSupplies());
            ViewFullBillCommand = new RelayCommand(ExecuteViewFullBill, () => SelectedEntry != null && !string.IsNullOrEmpty(SelectedEntry.ImagePath));
            ToggleSearchResultsCommand = new RelayCommand(() => ExecuteToggleSearchResults());

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
                // Clear search state silently to avoid infinite recursion
                _searchBarcode = string.Empty;
                OnPropertyChanged(nameof(SearchBarcode));
                FoundItem = null;
                
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
            IsEditing = false;
            ClearForm();
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

        private async Task ExecuteExplicitSearchItem()
        {
            if (IsMainResultsOpen && MainProductSearchResults.Count > 0)
            {
                SelectedMainProductResult = MainProductSearchResults[0];
            }
            else
            {
                await ExecuteSearchItem();
            }
        }

        private async Task ExecuteSearchItem()
        {
            if (string.IsNullOrWhiteSpace(SearchBarcode))
            {
                await LoadAllRecentSupplies();
                return;
            }

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
                    
                    // Clear search bar silently to keep filtered list
                    _searchBarcode = string.Empty;
                    OnPropertyChanged(nameof(SearchBarcode));
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

        private async Task ExecuteUpdateSupply(Stock? entry)
        {
            if (entry == null) return;
            
            try
            {
                StatusMessage = "Loading entry for update...";
                
                // 1. Setup Form State
                IsEditing = true;
                IsAddingNew = true;
                FormQuantity = entry.Quantity;
                TempImagePath = string.Empty;
                ManualSearchBarcode = entry.ProductId;
                
                // 2. Fetch the item to show in form
                using var conn = Data.DatabaseHelper.GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM Item WHERE itemId = @bid LIMIT 1;";
                cmd.Parameters.AddWithValue("@bid", entry.ProductId);

                using var reader = await cmd.ExecuteReaderAsync();
                if (reader.Read())
                {
                    FoundItem = new Item
                    {
                        ItemId = reader.GetString(reader.GetOrdinal("itemId")),
                        Description = reader.GetString(reader.GetOrdinal("Description")),
                        StockQuantity = reader.GetDouble(reader.GetOrdinal("StockQuantity"))
                    };
                    ManualSearchBarcode = FoundItem.Description; // Show description in update modal box
                    StatusMessage = "Editing supply for: " + FoundItem.Description;
                    
                    if (!string.IsNullOrEmpty(entry.ImagePath) && File.Exists(entry.ImagePath))
                    {
                        LoadFormImagePreview(entry.ImagePath);
                    }
                }
                
                SelectedEntry = entry; // Keep track of which entry we are editing
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load supply for update", ex);
                StatusMessage = "Error loading update form.";
            }
        }

        private async Task ExecuteExplicitManualSearch()
        {
            if (IsSearchResultsOpen && ProductSearchResults.Count > 0)
            {
                SelectedProductResult = ProductSearchResults[0];
            }
            else
            {
                await ExecuteFindManualItem();
            }
        }

        private async Task ExecuteFindManualItem(string? filter = null)
        {
            try
            {
                string queryText = filter ?? ManualSearchBarcode;
                StatusMessage = "Looking for items...";
                using var conn = Data.DatabaseHelper.GetConnection();
                using var cmd = conn.CreateCommand();
                
                if (string.IsNullOrWhiteSpace(queryText))
                {
                    cmd.CommandText = "SELECT * FROM Item ORDER BY Description LIMIT 50;";
                }
                else
                {
                    cmd.CommandText = "SELECT * FROM Item WHERE itemId = @bid OR Description LIKE @name LIMIT 50;";
                    cmd.Parameters.AddWithValue("@bid", queryText);
                    cmd.Parameters.AddWithValue("@name", $"%{queryText}%");
                }

                ProductSearchResults.Clear();
                using var reader = await cmd.ExecuteReaderAsync();
                while (reader.Read())
                {
                    ProductSearchResults.Add(new Item
                    {
                        ItemId = reader.GetString(reader.GetOrdinal("itemId")),
                        Description = reader.GetString(reader.GetOrdinal("Description")),
                        StockQuantity = reader.GetDouble(reader.GetOrdinal("StockQuantity"))
                    });
                }

                if (ProductSearchResults.Count > 0)
                {
                    IsSearchResultsOpen = true;
                    StatusMessage = $"Found {ProductSearchResults.Count} matching items.";
                }
                else
                {
                    IsSearchResultsOpen = false;
                    StatusMessage = "No matching items found.";
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Manual search failed", ex);
                StatusMessage = "Error finding item.";
            }
        }

        private async Task ExecuteToggleSearchResults()
        {
            if (IsSearchResultsOpen)
            {
                IsSearchResultsOpen = false;
            }
            else
            {
                // When toggling manually, if we have a full description, show all to allow selection of another
                bool isFullDescription = FoundItem != null && ManualSearchBarcode == FoundItem.Description;
                await ExecuteFindManualItem(isFullDescription ? "" : null);
                IsSearchResultsOpen = ProductSearchResults.Count > 0;
            }
        }

        private async Task ExecuteLiveMainSearch()
        {
            if (string.IsNullOrWhiteSpace(SearchBarcode)) return;

            try
            {
                using var conn = Data.DatabaseHelper.GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM Item WHERE itemId = @bid OR Description LIKE @name LIMIT 10;";
                cmd.Parameters.AddWithValue("@bid", SearchBarcode);
                cmd.Parameters.AddWithValue("@name", $"%{SearchBarcode}%");

                MainProductSearchResults.Clear();
                using var reader = await cmd.ExecuteReaderAsync();
                while (reader.Read())
                {
                    MainProductSearchResults.Add(new Item
                    {
                        ItemId = reader.GetString(reader.GetOrdinal("itemId")),
                        Description = reader.GetString(reader.GetOrdinal("Description")),
                        StockQuantity = reader.GetDouble(reader.GetOrdinal("StockQuantity"))
                    });
                }

                IsMainResultsOpen = MainProductSearchResults.Count > 0;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Live main search failed", ex);
            }
        }

        private void ExecuteSelectMainProduct(Item? item)
        {
            if (item == null) return;
            
            FoundItem = item;
            // Clear search bar silently to keep filtered list
            _searchBarcode = string.Empty;
            OnPropertyChanged(nameof(SearchBarcode));
            IsMainResultsOpen = false;
            _ = LoadSupplyHistory();
        }

        private void ExecuteSelectProduct(Item? item)
        {
            if (item == null) return;
            
            FoundItem = item;
            ManualSearchBarcode = item.Description; 
            IsSearchResultsOpen = false;
            StatusMessage = "Item selected: " + item.Description;
            _ = LoadSupplyHistory();
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
                LoadFormImagePreview(TempImagePath);
            }
        }

        private void LoadFormImagePreview(string path)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                FormImagePreview = bitmap;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error loading form image preview", ex);
                FormImagePreview = null;
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

                if (IsEditing && SelectedEntry != null)
                {
                    entry.Id = SelectedEntry.Id;
                    await _stockService.UpdateSupplyAsync(entry, TempImagePath);
                    StatusMessage = "Supply updated successfully!";
                }
                else
                {
                    await _stockService.RegisterSupplyAsync(entry, TempImagePath);
                    StatusMessage = "Supply registered successfully!";
                }
                
                IsAddingNew = false;
                
                // Refresh logic: If searching, refresh that item. If not, refresh all recent.
                if (string.IsNullOrWhiteSpace(SearchBarcode))
                    await LoadAllRecentSupplies();
                else
                    await ExecuteSearchItem(); 

                ClearForm();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to save supply", ex);
                MessageBox.Show("Error registration supply: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            FormImagePreview = null;
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
