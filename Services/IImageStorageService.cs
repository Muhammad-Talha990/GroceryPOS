using System.Threading.Tasks;

namespace GroceryPOS.Services
{
    public interface IImageStorageService
    {
        /// <summary>
        /// Validates, renames, and saves an image to local storage.
        /// </summary>
        /// <param name="sourceFilePath">Temporary path of the selected image.</param>
        /// <param name="billId">The generated Bill ID to use for renaming.</param>
        /// <returns>The destination path where the image was saved.</returns>
        Task<string> SaveBillImageAsync(string sourceFilePath, string billId);

        /// <summary>
        /// Deletes the image associated with a Bill ID.
        /// </summary>
        void DeleteBillImage(string billId);

        /// <summary>
        /// Returns the full local path for a stored image.
        /// </summary>
        string GetBillImagePath(string billId);
    }
}
