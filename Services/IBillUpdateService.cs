using System.Collections.Generic;
using System.Threading.Tasks;
using GroceryPOS.Models;

namespace GroceryPOS.Services
{
    public interface IBillUpdateService
    {
        /// <summary>
        /// Corrects an existing bill by creating a new one and marking the old one as replaced.
        /// </summary>
        /// <param name="originalBillId">ID of the bill to correct.</param>
        /// <param name="updatedItems">New list of items for the bill.</param>
        /// <param name="newDiscount">Updated discount amount.</param>
        /// <param name="newTax">Updated tax amount.</param>
        /// <param name="newCashReceived">Updated cash received.</param>
        /// <param name="userId">ID of the admin/user performing the update.</param>
        /// <returns>The newly generated corrected Bill.</returns>
        Task<Bill> UpdateBill(int originalBillId, List<BillDescription> updatedItems, double newDiscount, double newTax, double newCashReceived, int? userId);
    }
}
