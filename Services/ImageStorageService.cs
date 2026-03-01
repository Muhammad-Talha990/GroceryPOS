using System;
using System.IO;
using System.Threading.Tasks;
using GroceryPOS.Helpers;

namespace GroceryPOS.Services
{
    public class ImageStorageService : IImageStorageService
    {
        private const string RootFolder = @"C:\ProgramData\MyPOS\SupplierBills\";
        private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5MB
        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png" };

        public ImageStorageService()
        {
            EnsureDirectoryExists();
        }

        public async Task<string> SaveBillImageAsync(string sourceFilePath, string billId)
        {
            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException("Source image file not found.", sourceFilePath);

            var fileInfo = new FileInfo(sourceFilePath);
            
            // Validation: Size
            if (fileInfo.Length > MaxFileSizeBytes)
                throw new InvalidOperationException($"File size exceeds the 5MB limit ({fileInfo.Length / (1024 * 1024):N2} MB).");

            // Validation: Extension
            string extension = Path.GetExtension(sourceFilePath).ToLower();
            if (Array.IndexOf(AllowedExtensions, extension) < 0)
                throw new InvalidOperationException($"Invalid file type. Only {string.Join(", ", AllowedExtensions)} are allowed.");

            // Construct new filename: SUP-2026-0001.jpg
            string destFileName = $"{billId}{extension}";
            string destPath = Path.Combine(RootFolder, destFileName);

            // Copy to local storage
            try
            {
                using (var sourceStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
                using (var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
                {
                    await sourceStream.CopyToAsync(destStream);
                }
                
                AppLogger.Info($"Supplier Bill Image saved: {destPath}");
                return destPath;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to save bill image for {billId}", ex);
                throw new InvalidOperationException("Could not save the bill image. Please check permissions.", ex);
            }
        }

        public void DeleteBillImage(string billId)
        {
            try
            {
                foreach (var ext in AllowedExtensions)
                {
                    string path = Path.Combine(RootFolder, $"{billId}{ext}");
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        AppLogger.Info($"Deleted bill image: {path}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Error deleting image for bill {billId}", ex);
            }
        }

        public string GetBillImagePath(string billId)
        {
            foreach (var ext in AllowedExtensions)
            {
                string path = Path.Combine(RootFolder, $"{billId}{ext}");
                if (File.Exists(path)) return path;
            }
            return string.Empty;
        }

        private void EnsureDirectoryExists()
        {
            if (!Directory.Exists(RootFolder))
            {
                Directory.CreateDirectory(RootFolder);
                AppLogger.Info($"Created supplier bill storage directory: {RootFolder}");
            }
        }
    }
}
