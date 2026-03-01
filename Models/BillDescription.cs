using System.ComponentModel;

namespace GroceryPOS.Models
{
    /// <summary>
    /// Represents a single line item on a bill.
    /// Stores the UnitPrice at time of sale for price history integrity.
    /// Maps to the "BillDescription" table in SQLite.
    /// </summary>
    public class BillDescription : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Auto-increment primary key.</summary>
        public int Id { get; set; }

        /// <summary>Foreign key to Bill.bill_id.</summary>
        public int BillId { get; set; }

        /// <summary>Foreign key to Item.itemId (barcode).</summary>
        public string ItemId { get; set; } = string.Empty;

        private int _quantity;
        /// <summary>Quantity of this item sold.</summary>
        public int Quantity
        {
            get => _quantity;
            set
            {
                if (_quantity != value)
                {
                    _quantity = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Quantity)));
                    TotalPrice = _quantity * UnitPrice;
                }
            }
        }

        /// <summary>Unit price at time of sale (frozen for history).</summary>
        public double UnitPrice { get; set; }

        private double _totalPrice;
        /// <summary>Line total: Quantity × UnitPrice.</summary>
        public double TotalPrice
        {
            get => _totalPrice;
            set
            {
                if (_totalPrice != value)
                {
                    _totalPrice = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalPrice)));
                }
            }
        }

        // ── Convenience properties (not stored in DB) ──

        /// <summary>Item description, populated by JOIN or lookup.</summary>
        public string ItemDescription { get; set; } = string.Empty;
    }
}
