using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using GroceryPOS.Data;
using GroceryPOS.Data.Repositories;
using GroceryPOS.Helpers;
using GroceryPOS.Services;
using GroceryPOS.ViewModels;
using GroceryPOS.Views;

namespace GroceryPOS;

/// <summary>
/// Application entry point.
/// Initializes the database and manages login/main window lifecycle with Dependency Injection.
/// </summary>
public partial class App : Application
{
    private IServiceProvider _serviceProvider = null!;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // Global exception handling
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        // Auto-select text via preview mouseDown for standard experience
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(TextBox_PreviewMouseDown));

        try
        {
            // 1. Initialize database (Static setup)
            DatabaseInitializer.Initialize();

            // 2. Setup Dependency Injection
            ConfigureServices();

            // 3. Load In-Memory Cache (Resolved from DI)
            var cache = _serviceProvider.GetRequiredService<DataCacheService>();
            cache.LoadAllData();

            AppLogger.Info("Application started. Database and Safe Cache initialized with DI.");

            ShowLogin();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Application startup failed", ex);
            MessageBox.Show($"Failed to start application:\n{ex.Message}",
                "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void ConfigureServices()
    {
        var services = new ServiceCollection();

        // --- Data Layer (Repositories) ---
        services.AddSingleton<ItemRepository>();
        services.AddSingleton<UserRepository>();
        services.AddSingleton<BillRepository>();
        services.AddSingleton<BillReturnRepository>();
        services.AddSingleton<CustomerRepository>();
        services.AddSingleton<CreditPaymentRepository>();

        // --- Service Layer ---
        services.AddSingleton<DataCacheService>(); // Cache must be singleton for consistency
        services.AddSingleton<AuthService>();
        services.AddSingleton<IStockService, StockService>();
        services.AddSingleton<ItemService>();
        services.AddSingleton<BillService>();
        services.AddSingleton<CustomerService>();
        services.AddSingleton<CreditService>();
        services.AddSingleton<PrintService>();
        services.AddSingleton<ReportService>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<IReturnService, ReturnService>();

        // --- Supplier Bill Management ---
        services.AddSingleton<IImageStorageService, ImageStorageService>();
        services.AddSingleton<UniqueIdGenerator>();

        // --- ViewModels ---
        services.AddTransient<LoginViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ProductsViewModel>();
        services.AddTransient<BillingViewModel>();
        services.AddTransient<ReportsViewModel>();
        services.AddTransient<BackupViewModel>();
        services.AddTransient<SupplierBillsViewModel>();
        services.AddTransient<ReturnViewModel>();
        services.AddTransient<PendingPrintsViewModel>();
        services.AddTransient<CustomerManagementViewModel>();
        services.AddTransient<CustomerLedgerViewModel>();

        _serviceProvider = services.BuildServiceProvider();
    }

    private void ShowLogin()
    {
        var loginVM = _serviceProvider.GetRequiredService<LoginViewModel>();
        var loginView = new LoginView { DataContext = loginVM };

        loginVM.LoginSucceeded += () =>
        {
            loginView.Hide();
            ShowMainWindow();
        };

        loginView.Show();
    }

    private void ShowMainWindow()
    {
        var mainVM = _serviceProvider.GetRequiredService<MainViewModel>();
        var mainWindow = mainVM.InitializeView(); // Ensure the view is created with the resolved VM
        
        mainVM.LogoutRequested += () =>
        {
            mainWindow.Hide();
            ShowLogin();
        };

        mainWindow.Show();
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Error("UI Thread Exception", e.Exception);
        ShowErrorDialog("An unexpected error occurred in the application interface.", e.Exception);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            AppLogger.Error("Non-UI Thread Exception", ex);
            System.Windows.Application.Current.Dispatcher.Invoke(() => {
                ShowErrorDialog("A critical system error occurred.", ex);
            });
        }
    }

    private void ShowErrorDialog(string userMessage, Exception ex)
    {
        var detail = ex.Message;
        if (ex.InnerException != null) detail += $"\nInner Error: {ex.InnerException.Message}";

        MessageBox.Show(
            $"{userMessage}\n\nTechnical Details:\n{detail}\n\nThe error has been logged for review.",
            "System Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    // --- Select-all-on-focus helpers ---
    private static void TextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox tb && !tb.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            tb.Focus();
        }
    }
}
