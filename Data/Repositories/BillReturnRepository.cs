using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using GroceryPOS.Helpers;
using GroceryPOS.Models;

namespace GroceryPOS.Data.Repositories
{
    /// <summary>
    /// Data access for the BILL_RETURNS table.
    /// </summary>
    public class BillReturnRepository
    {
        /// <summary>
        /// Inserts a new return record.
        /// Assumes the connection/transaction is managed by the caller if needed.
        /// </summary>
        public void Insert(BillReturn billReturn, SqliteConnection? conn = null, SqliteTransaction? txn = null)
        {
            bool manageConnection = (conn == null);
            if (manageConnection) conn = DatabaseHelper.GetConnection();

            try
            {
                using var cmd = conn.CreateCommand();
                if (txn != null) cmd.Transaction = txn;

                cmd.CommandText = @"
                    INSERT INTO BILL_RETURNS (bill_id, product_id, return_quantity, original_bill_date, return_date, return_bill_id)
                    VALUES (@billId, @productId, @qty, @origDate, @retDate, @retBillId);
                ";
                cmd.Parameters.AddWithValue("@billId", billReturn.BillId);
                cmd.Parameters.AddWithValue("@productId", billReturn.ProductId);
                cmd.Parameters.AddWithValue("@qty", billReturn.ReturnQuantity);
                cmd.Parameters.AddWithValue("@origDate", billReturn.OriginalBillDate);
                cmd.Parameters.AddWithValue("@retDate", billReturn.ReturnDate);
                cmd.Parameters.AddWithValue("@retBillId", billReturn.ReturnBillId);

                cmd.ExecuteNonQuery();
            }
            finally
            {
                if (manageConnection) conn?.Dispose();
            }
        }

        /// <summary>
        /// Calculates the total quantity already returned for a specific product in a bill.
        /// </summary>
        public int GetTotalReturnedQuantity(int billId, string productId)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(return_quantity), 0) 
                FROM BILL_RETURNS 
                WHERE bill_id = @billId AND product_id = @productId;
            ";
            cmd.Parameters.AddWithValue("@billId", billId);
            cmd.Parameters.AddWithValue("@productId", productId);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>
        /// Gets all return records for a specific original bill.
        /// </summary>
        public List<BillReturn> GetByOriginalBillId(int billId)
        {
            var returns = new List<BillReturn>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT br.*, i.Description as ProductDesc
                FROM BILL_RETURNS br
                LEFT JOIN Item i ON br.product_id = i.itemId
                WHERE br.bill_id = @billId;
            ";
            cmd.Parameters.AddWithValue("@billId", billId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                returns.Add(new BillReturn
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    BillId = reader.GetInt32(reader.GetOrdinal("bill_id")),
                    ProductId = reader.GetString(reader.GetOrdinal("product_id")),
                    ReturnQuantity = reader.GetInt32(reader.GetOrdinal("return_quantity")),
                    OriginalBillDate = reader.GetString(reader.GetOrdinal("original_bill_date")),
                    ReturnDate = reader.GetString(reader.GetOrdinal("return_date")),
                    ReturnBillId = reader.GetString(reader.GetOrdinal("return_bill_id")),
                    ProductDescription = reader.IsDBNull(reader.GetOrdinal("ProductDesc")) ? "Unknown" : reader.GetString(reader.GetOrdinal("ProductDesc"))
                });
            }
            return returns;
        }
    }
}
