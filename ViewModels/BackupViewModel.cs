using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using GroceryPOS.Helpers;
using GroceryPOS.Services;
using Microsoft.Win32;

namespace GroceryPOS.ViewModels
{
    public class BackupViewModel : BaseViewModel
    {
        private readonly BackupService _backupService;
        public ObservableCollection<string> BackupFiles { get; set; } = new();

        private string _statusMessage = string.Empty;
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private string? _selectedBackupFile;
        public string? SelectedBackupFile { get => _selectedBackupFile; set => SetProperty(ref _selectedBackupFile, value); }

        public ICommand CreateBackupCommand { get; }
        public ICommand RestoreBackupCommand { get; }
        public ICommand BrowseRestoreCommand { get; }
        public ICommand OpenBackupFolderCommand { get; }
        public ICommand RefreshCommand { get; }

        public BackupViewModel(BackupService backupService)
        {
            _backupService = backupService;

            CreateBackupCommand = new RelayCommand(CreateBackup);
            RestoreBackupCommand = new RelayCommand(RestoreBackup);
            BrowseRestoreCommand = new RelayCommand(BrowseRestore);
            OpenBackupFolderCommand = new RelayCommand(OpenBackupFolder);
            RefreshCommand = new RelayCommand(LoadBackups);

            LoadBackups();
        }

        private void LoadBackups()
        {
            BackupFiles.Clear();
            foreach (var file in _backupService.GetBackupFiles())
                BackupFiles.Add(file);
        }

        private void CreateBackup()
        {
            try
            {
                var path = _backupService.CreateBackup();
                StatusMessage = $"✓ Backup created: {Path.GetFileName(path)}";
                LoadBackups();
            }
            catch (Exception ex)
            {
                StatusMessage = $"✗ Backup failed: {ex.Message}";
                AppLogger.Error("Backup failed", ex);
            }
        }

        private void RestoreBackup()
        {
            if (string.IsNullOrEmpty(SelectedBackupFile))
            {
                StatusMessage = "Please select a backup file to restore.";
                return;
            }

            var result = MessageBox.Show(
                "Are you sure you want to restore from this backup? The current database will be replaced.\n\nThe application will need to restart after restore.",
                "Confirm Restore",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _backupService.RestoreBackup(SelectedBackupFile);
                    StatusMessage = "✓ Database restored successfully! Please restart the application.";
                    MessageBox.Show("Database restored. Please restart the application.",
                        "Restore Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    StatusMessage = $"✗ Restore failed: {ex.Message}";
                    AppLogger.Error("Restore failed", ex);
                }
            }
        }

        private void BrowseRestore()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Database Files (*.db)|*.db",
                Title = "Select Backup File to Restore"
            };

            if (dialog.ShowDialog() == true)
            {
                SelectedBackupFile = dialog.FileName;
            }
        }

        private void OpenBackupFolder()
        {
            try
            {
                var folder = _backupService.GetBackupDirectory();
                System.Diagnostics.Process.Start("explorer.exe", folder);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Open backup folder failed", ex);
            }
        }
    }
}
