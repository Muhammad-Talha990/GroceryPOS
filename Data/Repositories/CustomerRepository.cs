using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using GroceryPOS.Helpers;
using GroceryPOS.Models;

namespace GroceryPOS.Data.Repositories
{
    public class CustomerRepository
    {
        public void Save(Customer customer)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Customers (Name, PrimaryPhone, Address, CreatedAt)
                VALUES (@name, @phone, @address, @created);
                SELECT last_insert_rowid();";
            
            cmd.Parameters.AddWithValue("@name", customer.Name);
            cmd.Parameters.AddWithValue("@phone", NormalizePhone(customer.PrimaryPhone));
            cmd.Parameters.AddWithValue("@address", customer.Address ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@created", customer.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));

            customer.CustomerId = Convert.ToInt32(cmd.ExecuteScalar());
        }

        public Customer? GetByPhone(string phone)
        {
            string normalized = NormalizePhone(phone);
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Customers WHERE PrimaryPhone = @phone";
            cmd.Parameters.AddWithValue("@phone", normalized);

            using var reader = cmd.ExecuteReader();
            if (reader.Read()) return MapCustomer(reader);
            return null;
        }

        public List<Customer> Search(string query)
        {
            string normalized = NormalizePhone(query);
            var customers = new List<Customer>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            
            // Unified search: by phone OR by name
            cmd.CommandText = @"
                SELECT c.*, 
                       (SELECT COUNT(*) FROM Bill b WHERE b.CustomerId = c.CustomerId AND b.Type = 'Sale') as BillCount,
                       (SELECT COALESCE(SUM(GrandTotal), 0) FROM Bill b WHERE b.CustomerId = c.CustomerId AND b.Type = 'Sale') as TotalAmount,
                       (SELECT MAX(BillDateTime) FROM Bill b WHERE b.CustomerId = c.CustomerId AND b.Type = 'Sale') as LastVisit
                FROM Customers c 
                WHERE (c.Name LIKE @nameQuery) ";

            if (!string.IsNullOrEmpty(normalized))
            {
                cmd.CommandText += " OR (c.PrimaryPhone LIKE @phoneQuery OR c.PrimaryPhone = @exactPhone)";
                cmd.Parameters.AddWithValue("@phoneQuery", "%" + normalized);
                cmd.Parameters.AddWithValue("@exactPhone", normalized);
            }

            cmd.CommandText += " ORDER BY BillCount DESC, c.Name ASC LIMIT 10;";
            cmd.Parameters.AddWithValue("@nameQuery", "%" + query + "%");

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                customers.Add(MapCustomer(reader));
            }
            return customers;
        }

        public Customer? GetById(int id)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Customers WHERE CustomerId = @id";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = cmd.ExecuteReader();
            if (reader.Read()) return MapCustomer(reader);
            return null;
        }

        private string NormalizePhone(string phone)
        {
            if (string.IsNullOrEmpty(phone)) return string.Empty;
            // Remove everything except digits
            var result = new System.Text.StringBuilder();
            foreach (char c in phone)
            {
                if (char.IsDigit(c)) result.Append(c);
            }
            return result.ToString();
        }

        private Customer MapCustomer(SqliteDataReader reader)
        {
            return new Customer
            {
                CustomerId = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                PrimaryPhone = reader.GetString(reader.GetOrdinal("PrimaryPhone")),
                Address = reader.IsDBNull(reader.GetOrdinal("Address")) ? null : reader.GetString(reader.GetOrdinal("Address")),
                CreatedAt = DateTime.TryParse(reader.GetString(reader.GetOrdinal("CreatedAt")), out var dt) ? dt : DateTime.Now,
                BillCount = reader.HasColumn("BillCount") ? reader.GetInt32(reader.GetOrdinal("BillCount")) : 0,
                TotalAmount = reader.HasColumn("TotalAmount") ? reader.GetDouble(reader.GetOrdinal("TotalAmount")) : 0,
                LastVisitDate = reader.HasColumn("LastVisit") && !reader.IsDBNull(reader.GetOrdinal("LastVisit")) 
                                ? (DateTime.TryParse(reader.GetString(reader.GetOrdinal("LastVisit")), out var lvd) ? lvd : null) 
                                : null
            };
        }
    }

    public static class SqliteExtensions
    {
        public static bool HasColumn(this SqliteDataReader reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
