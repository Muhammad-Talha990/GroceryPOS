using System.ComponentModel;

namespace GroceryPOS.Models
{
    /// <summary>
    /// Represents a single line item on a bill.
    /// Maps to the "BillItems" table in the normalized 3NF schema.
    /// </summary>
    public class BillDescription : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Database Internal ID (Primary Key).</summary>
        public int BillItemId { get; set; }

        /// <summary>Backward-compat alias.</summary>
        public int Id { get => BillItemId; set => BillItemId = value; }

        /// <summary>Foreign key to Bills table.</summary>
        public int BillId { get; set; }

        /// <summary>Internal Database ID of the Item.</summary>
        public int ItemInternalId { get; set; }

        /// <summary>Product ID as string (= ItemInternalId.ToString()).</summary>
        public string ItemId { get; set; } = string.Empty;

        /// <summary>Product barcode (may be null for items without barcode).</summary>
        public string? Barcode { get; set; }

        private double _quantity;
        /// <summary>Quantity of this item sold.</summary>
        public double Quantity
        {
            get => _quantity;
            set
            {
                if (_quantity != value)
                {
                    _quantity = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Quantity)));
                    TotalPrice = _quantity * UnitPrice - DiscountAmount;
                }
            }
        }

        /// <summary>Unit price at time of sale (frozen for history).</summary>
        public double UnitPrice { get; set; }

        private double _discountAmount;
        /// <summary>Flat discount applied to this specific line item.</summary>
        public double DiscountAmount
        {
            get => _discountAmount;
            set
            {
                if (_discountAmount != value)
                {
                    _discountAmount = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DiscountAmount)));
                    TotalPrice = Quantity * UnitPrice - _discountAmount;
                }
            }
        }

        private double _totalPrice;
        /// <summary>Line total: (Quantity × UnitPrice) - DiscountAmount.</summary>
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

        /// <summary>Item description, populated by JOIN or lookup.</summary>
        public string ItemDescription { get; set; } = string.Empty;
    }
}
