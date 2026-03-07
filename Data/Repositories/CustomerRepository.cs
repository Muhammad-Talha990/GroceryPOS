using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using GroceryPOS.Helpers;
using GroceryPOS.Models;

namespace GroceryPOS.Data.Repositories
{
    /// <summary>
    /// Data access for the Customers table.
    /// Supports full CRUD, soft-delete, credit querying, and filtered search.
    /// </summary>
    public class CustomerRepository
    {
        // ────────────────────────────────────────────
        //  WRITE operations
        // ────────────────────────────────────────────

        /// <summary>Inserts a new customer record.</summary>
        public void Save(Customer customer)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Customers (Name, FullName, PrimaryPhone, SecondaryPhone, Address, Address2, Address3, IsActive, CreatedAt)
                VALUES (@name, @fullName, @phone, @phone2, @address, @address2, @address3, 1, @created);
                SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("@name",     customer.FullName);
            cmd.Parameters.AddWithValue("@fullName", customer.FullName);
            cmd.Parameters.AddWithValue("@phone",    NormalizePhone(customer.PrimaryPhone));
            cmd.Parameters.AddWithValue("@phone2",   string.IsNullOrEmpty(customer.SecondaryPhone) ? (object)DBNull.Value : NormalizePhone(customer.SecondaryPhone));
            cmd.Parameters.AddWithValue("@address",  customer.Address ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@address2", customer.Address2 ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@address3", customer.Address3 ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@created",  customer.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));

            customer.CustomerId = Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>Updates an existing customer's editable fields.</summary>
        public void Update(Customer customer)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Customers
                SET Name           = @fullName,
                    FullName       = @fullName,
                    PrimaryPhone   = @phone,
                    SecondaryPhone = @phone2,
                    Address        = @address,
                    Address2       = @address2,
                    Address3       = @address3
                WHERE CustomerId = @id;";

            cmd.Parameters.AddWithValue("@fullName", customer.FullName);
            cmd.Parameters.AddWithValue("@phone",    NormalizePhone(customer.PrimaryPhone));
            cmd.Parameters.AddWithValue("@phone2",   string.IsNullOrEmpty(customer.SecondaryPhone) ? (object)DBNull.Value : NormalizePhone(customer.SecondaryPhone));
            cmd.Parameters.AddWithValue("@address",  customer.Address ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@address2", customer.Address2 ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@address3", customer.Address3 ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@id",       customer.CustomerId);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Soft-deletes a customer by setting IsActive = 0.</summary>
        public void SoftDelete(int customerId)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Customers SET IsActive = 0 WHERE CustomerId = @id;";
            cmd.Parameters.AddWithValue("@id", customerId);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Reactivates a previously deactivated customer.</summary>
        public void Reactivate(int customerId)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Customers SET IsActive = 1 WHERE CustomerId = @id;";
            cmd.Parameters.AddWithValue("@id", customerId);
            cmd.ExecuteNonQuery();
        }

        // ────────────────────────────────────────────
        //  READ operations
        // ────────────────────────────────────────────

        /// <summary>
        /// Returns all customers for the management grid (includes inactive ones).
        /// Each customer includes their pending credit balance.
        /// </summary>
        public List<Customer> GetAll(bool activeOnly = false)
        {
            var customers = new List<Customer>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT c.*,
                       COUNT(b.bill_id)                                          AS BillCount,
                       COALESCE(SUM(b.GrandTotal), 0)                           AS TotalAmount,
                       MAX(b.BillDateTime)                                       AS LastVisit,
                       COALESCE(SUM(CASE WHEN b.RemainingAmount > 0 AND b.Type = 'Sale' AND b.Status != 'Cancelled'
                                         THEN b.RemainingAmount ELSE 0 END), 0) AS PendingCredit
                FROM Customers c
                LEFT JOIN Bill b ON c.CustomerId = b.CustomerId AND b.Type = 'Sale'
                " + (activeOnly ? "WHERE c.IsActive = 1 " : "") + @"
                GROUP BY c.CustomerId
                ORDER BY c.FullName ASC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                customers.Add(MapCustomer(reader));

            return customers;
        }

        /// <summary>
        /// Searches active customers by name or phone (used in billing dropdown).
        /// Returns max 10 results.
        /// </summary>
        public List<Customer> Search(string query)
        {
            string normalized = NormalizePhone(query);
            var customers = new List<Customer>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
                SELECT c.*,
                       COUNT(b.bill_id)                                          AS BillCount,
                       COALESCE(SUM(b.GrandTotal), 0)                           AS TotalAmount,
                       MAX(b.BillDateTime)                                       AS LastVisit,
                       COALESCE(SUM(CASE WHEN b.RemainingAmount > 0 AND b.Type = 'Sale' AND b.Status != 'Cancelled'
                                         THEN b.RemainingAmount ELSE 0 END), 0) AS PendingCredit
                FROM Customers c
                LEFT JOIN Bill b ON c.CustomerId = b.CustomerId AND b.Type = 'Sale'
                WHERE c.IsActive = 1";

            if (!string.IsNullOrEmpty(normalized))
            {
                cmd.CommandText += @"
                  AND (COALESCE(c.FullName, c.Name) LIKE @nameQuery
                       OR c.PrimaryPhone  LIKE @phoneQuery
                       OR c.PrimaryPhone  = @exactPhone
                       OR c.SecondaryPhone LIKE @phoneQuery
                       OR c.SecondaryPhone = @exactPhone)";
                cmd.Parameters.AddWithValue("@phoneQuery", "%" + normalized);
                cmd.Parameters.AddWithValue("@exactPhone", normalized);
            }
            else
            {
                cmd.CommandText += @"
                  AND (COALESCE(c.FullName, c.Name) LIKE @nameQuery)";
            }

            cmd.CommandText += @"
                GROUP BY c.CustomerId
                ORDER BY BillCount DESC, COALESCE(c.FullName, c.Name) ASC
                LIMIT 10;";

            cmd.Parameters.AddWithValue("@nameQuery", "%" + query + "%");

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                customers.Add(MapCustomer(reader));

            return customers;
        }

        public Customer? GetByPhone(string phone)
        {
            string normalized = NormalizePhone(phone);
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT *, 0 AS BillCount, 0.0 AS TotalAmount, NULL AS LastVisit, 0.0 AS PendingCredit
                FROM Customers
                WHERE PrimaryPhone = @phone OR SecondaryPhone = @phone";
            cmd.Parameters.AddWithValue("@phone", normalized);

            using var reader = cmd.ExecuteReader();
            if (reader.Read()) return MapCustomer(reader);
            return null;
        }

        public Customer? GetById(int id)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT c.*,
                       COUNT(b.bill_id)                                          AS BillCount,
                       COALESCE(SUM(b.GrandTotal), 0)                           AS TotalAmount,
                       MAX(b.BillDateTime)                                       AS LastVisit,
                       COALESCE(SUM(CASE WHEN b.RemainingAmount > 0 AND b.Type = 'Sale' AND b.Status != 'Cancelled'
                                         THEN b.RemainingAmount ELSE 0 END), 0) AS PendingCredit
                FROM Customers c
                LEFT JOIN Bill b ON c.CustomerId = b.CustomerId AND b.Type = 'Sale'
                WHERE c.CustomerId = @id
                GROUP BY c.CustomerId;";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = cmd.ExecuteReader();
            if (reader.Read()) return MapCustomer(reader);
            return null;
        }

        /// <summary>Returns total pending credit for a specific customer.</summary>
        public double GetPendingCredit(int customerId)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(RemainingAmount), 0)
                FROM Bill
                WHERE CustomerId = @cid
                  AND RemainingAmount > 0
                  AND Type = 'Sale'
                  AND Status != 'Cancelled';";
            cmd.Parameters.AddWithValue("@cid", customerId);
            return Convert.ToDouble(cmd.ExecuteScalar());
        }

        // ────────────────────────────────────────────
        //  Private helpers
        // ────────────────────────────────────────────

        public string NormalizePhone(string phone)
        {
            if (string.IsNullOrEmpty(phone)) return string.Empty;
            var result = new System.Text.StringBuilder();
            foreach (char c in phone)
                if (char.IsDigit(c)) result.Append(c);
            return result.ToString();
        }

        private Customer MapCustomer(SqliteDataReader reader)
        {
            // FullName takes precedence; fall back to Name for old rows
            string fullName = reader.HasColumn("FullName") && !reader.IsDBNull(reader.GetOrdinal("FullName"))
                ? reader.GetString(reader.GetOrdinal("FullName"))
                : reader.GetString(reader.GetOrdinal("Name"));

            return new Customer
            {
                CustomerId    = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                FullName      = string.IsNullOrEmpty(fullName)
                                    ? reader.GetString(reader.GetOrdinal("Name"))
                                    : fullName,
                PrimaryPhone  = reader.GetString(reader.GetOrdinal("PrimaryPhone")),
                SecondaryPhone= reader.IsDBNull(reader.GetOrdinal("SecondaryPhone")) ? null : reader.GetString(reader.GetOrdinal("SecondaryPhone")),
                Address       = reader.IsDBNull(reader.GetOrdinal("Address")) ? null : reader.GetString(reader.GetOrdinal("Address")),
                Address2      = reader.IsDBNull(reader.GetOrdinal("Address2")) ? null : reader.GetString(reader.GetOrdinal("Address2")),
                Address3      = reader.IsDBNull(reader.GetOrdinal("Address3")) ? null : reader.GetString(reader.GetOrdinal("Address3")),
                IsActive      = reader.HasColumn("IsActive") ? reader.GetInt32(reader.GetOrdinal("IsActive")) != 0 : true,
                CreatedAt     = DateTime.TryParse(reader.GetString(reader.GetOrdinal("CreatedAt")), out var dt) ? dt : DateTime.Now,
                BillCount     = reader.HasColumn("BillCount")      ? reader.GetInt32(reader.GetOrdinal("BillCount"))    : 0,
                TotalAmount   = reader.HasColumn("TotalAmount")    ? reader.GetDouble(reader.GetOrdinal("TotalAmount")) : 0,
                PendingCredit = reader.HasColumn("PendingCredit")  ? reader.GetDouble(reader.GetOrdinal("PendingCredit")) : 0,
                LastVisitDate = reader.HasColumn("LastVisit") && !reader.IsDBNull(reader.GetOrdinal("LastVisit"))
                                    ? (DateTime.TryParse(reader.GetString(reader.GetOrdinal("LastVisit")), out var lvd) ? lvd : null)
                                    : null
            };
        }
    }

    // Keep extension class in the same namespace (was already here)
    public static class SqliteExtensions
    {
        public static bool HasColumn(this SqliteDataReader reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
                if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
