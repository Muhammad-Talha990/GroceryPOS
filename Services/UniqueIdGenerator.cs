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
                // Find the highest sequence number for the current year
                cmd.CommandText = "SELECT bill_id FROM stock WHERE bill_id LIKE @prefix ORDER BY bill_id DESC LIMIT 1;";
                cmd.Parameters.AddWithValue("@prefix", $"{prefix}%");
                
                var result = cmd.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                {
                    string lastId = result.ToString()!;
                    // Extract the last 4 digits (assuming format SUP-YYYY-XXXX)
                    if (lastId.Length >= 4)
                    {
                        string suffix = lastId.Substring(lastId.LastIndexOf('-') + 1);
                        if (int.TryParse(suffix, out int lastNumber))
                        {
                            nextNumber = lastNumber + 1;
                        }
                    }
                }
            }

            return $"{prefix}{nextNumber:D4}";
        }
    }
}
