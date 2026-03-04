using System;
using System.Collections.Generic;
using GroceryPOS.Data.Repositories;
using GroceryPOS.Models;

namespace GroceryPOS.Services
{
    public class CustomerService
    {
        private readonly CustomerRepository _customerRepo;
        private readonly BillRepository _billRepo;

        public CustomerService(CustomerRepository customerRepo, BillRepository billRepo)
        {
            _customerRepo = customerRepo;
            _billRepo = billRepo;
        }

        public void RegisterCustomer(Customer customer)
        {
            if (string.IsNullOrWhiteSpace(customer.Name))
                throw new ArgumentException("Customer name is required.");
            
            if (string.IsNullOrWhiteSpace(customer.PrimaryPhone))
                throw new ArgumentException("Primary phone is required.");

            string rawPhone = customer.PrimaryPhone.Trim();
            string normalized = NormalizePhone(rawPhone);
            if (!rawPhone.StartsWith("0") || normalized.Length != 11)
                throw new ArgumentException("Invalid primary phone format. Must start with '0' and be 11 digits.");

            if (_customerRepo.GetByPhone(normalized) != null)
                throw new InvalidOperationException("A customer with this primary phone number already exists.");

            customer.PrimaryPhone = normalized;

            if (!string.IsNullOrWhiteSpace(customer.SecondaryPhone))
            {
                string rawPhone2 = customer.SecondaryPhone.Trim();
                string normalized2 = NormalizePhone(rawPhone2);
                if (!rawPhone2.StartsWith("0") || normalized2.Length != 11)
                    throw new ArgumentException("Invalid secondary phone format. Must start with '0' and be 11 digits.");
                
                if (normalized == normalized2)
                    throw new ArgumentException("Primary and secondary phone numbers cannot be the same.");

                if (_customerRepo.GetByPhone(normalized2) != null)
                    throw new InvalidOperationException("A customer with this secondary phone number already exists.");
                
                customer.SecondaryPhone = normalized2;
            }

            _customerRepo.Save(customer);
        }

        public Customer? GetCustomerByPhone(string phone)
        {
            return _customerRepo.GetByPhone(NormalizePhone(phone));
        }

        public List<Customer> SearchCustomers(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<Customer>();
            return _customerRepo.Search(query);
        }

        public (int BillCount, double TotalAmount) GetCustomerStats(int customerId)
        {
            return _billRepo.GetCustomerStats(customerId);
        }

        public Bill? GetLatestBill(int customerId)
        {
            return _billRepo.GetLatestBillByCustomerId(customerId);
        }

        public string NormalizePhone(string phone)
        {
            if (string.IsNullOrEmpty(phone)) return string.Empty;
            var result = new System.Text.StringBuilder();
            foreach (char c in phone)
            {
                if (char.IsDigit(c)) result.Append(c);
            }
            return result.ToString();
        }
    }
}
