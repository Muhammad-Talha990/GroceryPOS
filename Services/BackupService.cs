using System;
using System.IO;
using System.Linq;
using GroceryPOS.Helpers;

namespace GroceryPOS.Services
{
    public class BackupService
    {
        private readonly string _dbPath;
        private readonly string _backupDirectory;

        public BackupService()
        {
            _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GroceryPOS.db");
            _backupDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups");
            if (!Directory.Exists(_backupDirectory))
                Directory.CreateDirectory(_backupDirectory);
        }

        public string CreateBackup()
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var backupFile = Path.Combine(_backupDirectory, $"GroceryPOS_Backup_{timestamp}.db");
                File.Copy(_dbPath, backupFile, overwrite: true);
                AppLogger.Info($"Database backup created: {backupFile}");
                return backupFile;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Backup creation failed", ex);
                throw;
            }
        }

        public void RestoreBackup(string backupFilePath)
        {
            try
            {
                if (!File.Exists(backupFilePath))
                    throw new FileNotFoundException("Backup file not found.", backupFilePath);

                // Create a safety backup before restore
                var safetyBackup = Path.Combine(_backupDirectory, $"PreRestore_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.db");
                File.Copy(_dbPath, safetyBackup, overwrite: true);

                File.Copy(backupFilePath, _dbPath, overwrite: true);
                AppLogger.Info($"Database restored from: {backupFilePath}");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Database restore failed", ex);
                throw;
            }
        }

        public string[] GetBackupFiles()
        {
            return Directory.GetFiles(_backupDirectory, "*.db")
                .OrderByDescending(f => File.GetCreationTime(f))
                .ToArray();
        }

        public string GetBackupDirectory() => _backupDirectory;
    }
}
