using System.ComponentModel;

namespace GroceryPOS.Models
{
    /// <summary>
    /// Represents a single item in the billing cart.
    /// </summary>
    public class CartItem : INotifyPropertyChanged
    {
        /// <summary>Barcode (FK to Item.itemId).</summary>
        public string ItemId { get; set; } = string.Empty;

        /// <summary>Item description for display.</summary>
        public string ItemDescription { get; set; } = string.Empty;

        /// <summary>Unit price at time of adding to cart.</summary>
        public double UnitPrice { get; set; }

        /// <summary>Maximum available stock for this item.</summary>
        public double AvailableStock { get; set; }

        private int _quantity = 1;
        /// <summary>Quantity in cart.</summary>
        public int Quantity
        {
            get => _quantity;
            set
            {
                _quantity = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Quantity)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalPrice)));
            }
        }

        /// <summary>Line total: UnitPrice × Quantity.</summary>
        public double TotalPrice => UnitPrice * Quantity;

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
