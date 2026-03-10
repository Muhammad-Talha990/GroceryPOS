using System;
using GroceryPOS.Data;

namespace GroceryPOS.Services
{
    /// <summary>
    /// Generates unique professional Bill IDs for Stock entries.
    /// Format: SUP-[YEAR]-[SEQUENCE] (e.g., SUP-2026-0001).
    /// </summary>
    public class UniqueIdGenerator
    {
        public string GenerateSupplierBillId()
        {
            var year = DateTime.Now.Year;
            var prefix = $"SUP-{year}-";
            int nextNumber = 1;

            using (var conn = DatabaseHelper.GetConnection())
            using (var cmd = conn.CreateCommand())
            {
                // Count supply entries this year to determine the next sequence number
                cmd.CommandText = @"
                    SELECT COUNT(*) FROM InventoryLogs 
                    WHERE ReferenceType = 'Supply' 
                      AND LogDate >= @yearStart;";
                cmd.Parameters.AddWithValue("@yearStart", $"{year}-01-01 00:00:00");
                
                var result = cmd.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                {
                    nextNumber = Convert.ToInt32(result) + 1;
                }
            }

            return $"{prefix}{nextNumber:D4}";
        }
    }
}
