using System.ComponentModel;

namespace GroceryPOS.Models
{
    /// <summary>
    /// Represents a single item in the billing cart.
    /// </summary>
    public class CartItem : INotifyPropertyChanged
    {
        /// <summary>Product ID (FK to Item.Id).</summary>
        public string ItemId { get; set; } = string.Empty;

        /// <summary>Product barcode for display (may be empty).</summary>
        public string? Barcode { get; set; }

        /// <summary>Item description for display.</summary>
        public string ItemDescription { get; set; } = string.Empty;

        /// <summary>Unit price at time of adding to cart.</summary>
        public double UnitPrice { get; set; }

        private double _availableStock;
        /// <summary>Maximum available stock for this item.</summary>
        public double AvailableStock
        {
            get => _availableStock;
            set
            {
                if (_availableStock != value)
                {
                    _availableStock = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvailableStock)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMaxQuantity)));
                }
            }
        }

        public bool IsMaxQuantity => Quantity >= AvailableStock && AvailableStock > 0;

        private double _quantity = 1;
        /// <summary>Quantity in cart.</summary>
        public double Quantity
        {
            get => _quantity;
            set
            {
                if (_quantity != value)
                {
                    _quantity = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Quantity)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalPrice)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMaxQuantity)));
                }
            }
        }

        /// <summary>Line total: UnitPrice × Quantity.</summary>
        public double TotalPrice => UnitPrice * Quantity;

        private bool _isCopied;
        /// <summary>Indicates if this item was copied from a previous bill.</summary>
        public bool IsCopied
        {
            get => _isCopied;
            set
            {
                if (_isCopied != value)
                {
                    _isCopied = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCopied)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
