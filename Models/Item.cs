namespace GroceryPOS.Models
{
    /// <summary>
    /// Represents a product/item in the inventory.
    /// Maps to the "Items" table in the normalized 3NF schema.
    /// </summary>
    public class Item
    {
        /// <summary>Database Internal ID (Primary Key).</summary>
        public int Id { get; set; }

        /// <summary>Product Barcode (optional, unique if provided).</summary>
        public string? Barcode { get; set; }

        /// <summary>String representation of the database Id – used as the canonical product identifier.</summary>
        public string ItemId
        {
            get => Id.ToString();
            set { if (int.TryParse(value, out var v)) Id = v; }
        }

        /// <summary>Product description / name.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Cost price (purchase price from supplier).</summary>
        public double CostPrice { get; set; }

        /// <summary>Sale price (selling price to customer).</summary>
        public double SalePrice { get; set; }

        /// <summary>Foreign Key linking to Categories table.</summary>
        public int? CategoryId { get; set; }

        /// <summary>Joined Category Name from Categories table.</summary>
        public string? CategoryName { get; set; }

        /// <summary>Compatibility shim for old code.</summary>
        public string? ItemCategory { get => CategoryName; set => CategoryName = value; }

        /// <summary>Current stock quantity available (calculated from InventoryLogs).</summary>
        public double StockQuantity { get; set; }

        /// <summary>Minimum stock level before alerting.</summary>
        public double MinStockThreshold { get; set; }

        /// <summary>Helper property for UI status.</summary>
        public bool IsLowStock => StockQuantity <= MinStockThreshold;
    }
}
