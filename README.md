# GroceryPOS

A desktop Point of Sale (POS) system built for grocery stores, developed with **C# WPF (.NET 8)** and **SQLite**.

## Features

- **Billing & Invoicing** — Multi-tab billing, barcode scanning, cash & online payments
- **Customer Management** — Registered customers with credit tracking, multiple addresses & phone numbers
- **Credit System** — Partial payments, due amount tracking, customer ledger with full payment history
- **Product Management** — Add/edit/delete items, cost & sale price tracking, low stock alerts
- **Stock Management** — Supplier bill recording with image attachments, inventory logs
- **Returns & Refunds** — Item-level returns with cash refund or credit adjustment
- **Reports** — Daily sales reports, bill filtering (normal/credit), date range queries
- **Thermal Printing** — 80mm receipt printing with auto-detect printer, PDF export
- **Dashboard** — Real-time stats: sales, returns, credit, cash in drawer, online payments

## Tech Stack

| Layer | Technology |
|-------|-----------|
| UI | WPF (XAML) |
| Framework | .NET 8 |
| Architecture | MVVM |
| Database | SQLite 3 (Microsoft.Data.Sqlite) |
| Printing | System.Drawing.Printing |

## Project Structure

```
GroceryPOS/
├── Models/          # Data models (Bill, Customer, Item, Stock, etc.)
├── ViewModels/      # MVVM ViewModels
├── Views/           # WPF XAML views
│   └── Controls/    # Reusable UI controls
├── Services/        # Business logic layer
├── Data/
│   ├── Repositories/  # SQLite data access
│   ├── DatabaseHelper.cs
│   └── DatabaseInitializer.cs
├── Converters/      # XAML value converters
├── Helpers/         # Focus, password, logging helpers
├── Themes/          # WPF styles and themes
└── Exceptions/      # Custom exception types
```

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows (WPF is Windows-only)

### Build & Run

```bash
dotnet build GroceryPOS-master.sln
dotnet run --project GroceryPOS.csproj
```

The SQLite database is created automatically on first run.

## Database

Normalized 3NF schema with automatic migration via `PRAGMA user_version`. See [DATABASE_DESIGN.md](DATABASE_DESIGN.md) for full documentation.

## License

This project is for educational and personal use.
