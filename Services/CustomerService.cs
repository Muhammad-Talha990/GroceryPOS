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

            // Constraint: Must start with 0 and be 11 digits
            if (!rawPhone.StartsWith("0") || normalized.Length != 11)
            {
                throw new ArgumentException("Invalid phone format. Phone number must start with '0' and be exactly 11 digits.");
            }

            if (_customerRepo.GetByPhone(normalized) != null)
                throw new InvalidOperationException("A customer with this phone number already exists.");

            customer.PrimaryPhone = normalized;
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
