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
    /// ViewModel for the Items (Products) management screen.
    /// Provides CRUD operations for the Item table.
    /// Category is now a free-text field instead of a foreign key.
    /// </summary>
    public class ProductsViewModel : BaseViewModel
    {
        private readonly ItemService _itemService;
        private readonly IStockService _stockService;

        public ObservableCollection<Item> Products { get; set; } = new();
        public ObservableCollection<string> Categories { get; set; } = new();

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { SetProperty(ref _searchText, value); SearchProducts(); }
        }

        private Item? _selectedProduct;
        public Item? SelectedProduct
        {
            get => _selectedProduct;
            set
            {
                if (SetProperty(ref _selectedProduct, value))
                    LoadProductToForm(value);
            }
        }

        // ── Form Fields ──
        private string _formName = string.Empty;
        public string FormName { get => _formName; set => SetProperty(ref _formName, value); }

        private string _formBarcode = string.Empty;
        public string FormBarcode { get => _formBarcode; set => SetProperty(ref _formBarcode, value); }

        private string _formCategory = string.Empty;
        public string FormCategory { get => _formCategory; set => SetProperty(ref _formCategory, value); }

        private string _formCostPrice = "0";
        public string FormCostPrice { get => _formCostPrice; set => SetProperty(ref _formCostPrice, value); }

        private string _formSalePrice = "0";
        public string FormSalePrice { get => _formSalePrice; set => SetProperty(ref _formSalePrice, value); }

        private string _formStockQuantity = "0";
        public string FormStockQuantity { get => _formStockQuantity; set => SetProperty(ref _formStockQuantity, value); }

        private string _formMinStockThreshold = "10";
        public string FormMinStockThreshold { get => _formMinStockThreshold; set => SetProperty(ref _formMinStockThreshold, value); }

        private bool _isEditing;
        public bool IsEditing 
        { 
            get => _isEditing; 
            set 
            { 
                if (SetProperty(ref _isEditing, value))
                    OnPropertyChanged(nameof(IsQuantityEditable));
            } 
        }

        public bool IsQuantityEditable => !IsEditing;

        private string _statusMessage = string.Empty;
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        // Store original barcode when editing (since barcode is PK and might change)
        private string? _originalBarcode;

        public ICommand AddCommand { get; }
        public ICommand UpdateCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand ClearFormCommand { get; }
        public ICommand RefreshCommand { get; }

        public ProductsViewModel(ItemService itemService, IStockService stockService)
        {
            _itemService = itemService;
            _stockService = stockService;

            AddCommand = new RelayCommand(AddProduct);
            UpdateCommand = new RelayCommand(UpdateProduct);
            DeleteCommand = new RelayCommand(DeleteProduct);
            ClearFormCommand = new RelayCommand(ClearForm);
            RefreshCommand = new RelayCommand(ExecuteRefreshProducts);

            // Subscribe to real-time stock updates
            _stockService.StockChanged += LoadProducts;

            LoadCategories();
            LoadProducts();
        }

        /// <summary>Called by MainViewModel when this page becomes the active view.</summary>
        public void OnActivated() => LoadProducts();

        private void LoadCategories()
        {
            Categories.Clear();
            foreach (var cat in _itemService.GetAllCategories())
                Categories.Add(cat);
        }

        private void LoadProducts()
        {
            try
            {
                var products = string.IsNullOrWhiteSpace(SearchText)
                    ? _itemService.GetAllItems()
                    : _itemService.SearchItems(SearchText);

                Dispatch(() =>
                {
                    Products.Clear();
                    foreach (var p in products)
                        Products.Add(p);
                });
            }
            catch (Exception ex)
            {
                AppLogger.Error("LoadProducts failed", ex);
            }
        }

        private void ExecuteRefreshProducts()
        {
            SearchText = string.Empty;
            // LoadProducts() is called by the SearchText setter
        }

        private void ClearForm()
        {
            try
            {
                IsEditing = false;
                SelectedProduct = null;
                _originalBarcode = null;
                FormName = string.Empty;
                FormBarcode = string.Empty;
                FormCategory = string.Empty;
                FormCostPrice = "0";
                FormSalePrice = "0";
                FormStockQuantity = "0.00";
                FormMinStockThreshold = "10";
                StatusMessage = "Form cleared.";
            }
            catch (Exception ex)
            {
                AppLogger.Error("ClearForm failed", ex);
                StatusMessage = $"✗ Error clearing form: {ex.Message}";
                ShowPopupError($"Error clearing form: {ex.Message}");
            }
        }

        private void SearchProducts() => LoadProducts();

        private void LoadProductToForm(Item? item)
        {
            if (item == null) { ClearForm(); return; }

            IsEditing = true;
            _originalBarcode = item.Barcode;
            FormName = item.Description;
            FormBarcode = item.Barcode ?? string.Empty;
            FormCategory = item.ItemCategory ?? string.Empty;
            FormCostPrice = item.CostPrice.ToString("F2");
            FormSalePrice = item.SalePrice.ToString("F2");
            FormStockQuantity = item.StockQuantity.ToString("F2");
            FormMinStockThreshold = item.MinStockThreshold.ToString("F2");
        }

        private void AddProduct()
        {
            try
            {
                AppLogger.Info("AddProduct started.");
                if (!ValidateForm()) return;

                var item = new Item
                {
                    Barcode = string.IsNullOrWhiteSpace(FormBarcode) ? null : FormBarcode.Trim(),
                    Description = FormName.Trim(),
                    CostPrice = double.Parse(FormCostPrice),
                    SalePrice = double.Parse(FormSalePrice),
                    ItemCategory = string.IsNullOrWhiteSpace(FormCategory) ? null : FormCategory.Trim(),
                    StockQuantity = double.TryParse(FormStockQuantity, out var stock) ? stock : 0,
                    MinStockThreshold = double.Parse(FormMinStockThreshold)
                };

                _itemService.AddItem(item);
                StatusMessage = $"✓ Item '{item.Description}' added successfully!";
                ShowPopupSuccess($"'{item.Description}' added successfully!");

                ClearForm();
                LoadProducts();
                LoadCategories();
            }
            catch (Exception ex)
            {
                var errorMsg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                StatusMessage = $"✗ Error: {errorMsg}";
                ShowPopupError(errorMsg);
                AppLogger.Error("Add item failed", ex);
            }
        }

        private void UpdateProduct()
        {
            try
            {
                if (SelectedProduct == null) { StatusMessage = "Please select an item to update."; ShowPopupError("Please select an item to update."); return; }
                if (!ValidateForm()) return;

                var newBarcode = string.IsNullOrWhiteSpace(FormBarcode) ? null : FormBarcode.Trim();
                var item = new Item
                {
                    Id = SelectedProduct.Id,
                    Barcode = newBarcode,
                    Description = FormName.Trim(),
                    CostPrice = double.Parse(FormCostPrice),
                    SalePrice = double.Parse(FormSalePrice),
                    ItemCategory = string.IsNullOrWhiteSpace(FormCategory) ? null : FormCategory.Trim(),
                    StockQuantity = double.Parse(FormStockQuantity), // Preserve current stock
                    MinStockThreshold = double.Parse(FormMinStockThreshold)
                };

                // Update using original barcode (handles PK change if any via ON UPDATE CASCADE)
                _itemService.UpdateItem(item, _originalBarcode!);

                StatusMessage = $"✓ Item '{FormName}' updated!";
                ShowPopupSuccess($"'{FormName}' updated successfully!");
                ClearForm();
                LoadProducts();
                LoadCategories();
            }
            catch (Exception ex)
            {
                var errorMsg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                StatusMessage = $"✗ Error: {errorMsg}";
                ShowPopupError(errorMsg);
                AppLogger.Error("Update item failed", ex);
            }
        }

        private void DeleteProduct()
        {
            if (SelectedProduct == null) { StatusMessage = "Please select an item to delete."; ShowPopupError("Please select an item to delete."); return; }

            // Capture item details BEFORE deletion, because UI might null SelectedProduct when collection changes
            int itemId = SelectedProduct.Id;
            string description = SelectedProduct.Description;

            var result = MessageBox.Show($"Are you sure you want to delete '{description}'?\n\nWarning: This will permanently remove the item from the database.",
                "Confirm Permanent Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _itemService.DeleteItem(itemId);
                    StatusMessage = $"✓ Item '{description}' permanently deleted.";
                    ClearForm();
                    LoadProducts();
                    LoadCategories();
                }
                catch (Exception ex)
                {
                    var errorMsg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    if (errorMsg.Contains("FOREIGN KEY constraint failed") || errorMsg.Contains("REFERENCE constraint"))
                    {
                        StatusMessage = "✗ Cannot delete: This item is linked to existing bill records.";
                        ShowPopupError("Cannot delete: This item is linked to existing bill records.");
                    }
                    else
                    {
                        StatusMessage = $"✗ Error deleting item: {errorMsg}";
                        ShowPopupError($"Error deleting item: {errorMsg}");
                    }
                    AppLogger.Error("Delete item failed", ex);
                }
            }
        }

        private bool ValidateForm()
        {
            if (string.IsNullOrWhiteSpace(FormName))
            { StatusMessage = "Item description is required."; ShowPopupError("Item description is required."); return false; }
            if (!double.TryParse(FormCostPrice, out var cost) || cost < 0)
            { StatusMessage = "Invalid cost price."; ShowPopupError("Invalid cost price."); return false; }
            if (!double.TryParse(FormSalePrice, out var sale) || sale < 0)
            { StatusMessage = "Invalid sale price."; ShowPopupError("Invalid sale price."); return false; }
            // Note: FormStockQuantity is read-only in UI, so validation is less critical but kept for format check
            if (!double.TryParse(FormStockQuantity, out _))
            { StatusMessage = "Invalid stock quantity format."; ShowPopupError("Invalid stock quantity format."); return false; }
            if (!double.TryParse(FormMinStockThreshold, out var threshold) || threshold < 0)
            { StatusMessage = "Invalid threshold."; ShowPopupError("Invalid threshold."); return false; }

            // Cost Price > Sale Price Validation
            if (cost > sale)
            {
                var result = MessageBox.Show($"Warning: The Cost Price (Rs. {cost:F2}) is higher than the Sale Price (Rs. {sale:F2}).\n\nYou will be selling this item at a loss. Are you sure you want to continue?", 
                    "Price Validation Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.No)
                {
                    StatusMessage = "✗ Operation cancelled due to price discrepancy.";
                    ShowPopupError("Operation cancelled due to price discrepancy.");
                    return false;
                }
            }

            return true;
        }
    }
}
