namespace GroceryPOS.Models
{
    /// <summary>
    /// Represents a product/item in the inventory.
    /// Primary key is the barcode (TEXT), not an auto-increment integer.
    /// Maps to the "Item" table in SQLite.
    /// </summary>
    public class Item
    {
        /// <summary>Barcode — serves as the primary key (TEXT).</summary>
        public string ItemId { get; set; } = string.Empty;

        /// <summary>Product description / name.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Cost price (purchase price from supplier).</summary>
        public double CostPrice { get; set; }

        /// <summary>Sale price (selling price to customer).</summary>
        public double SalePrice { get; set; }

        /// <summary>Category name (free text, e.g. "Dairy", "Beverages").</summary>
        public string? ItemCategory { get; set; }
    }
}
