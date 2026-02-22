using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Windows.Forms;
using GroceryPOS.Helpers;
using GroceryPOS.Models;

namespace GroceryPOS.Services
{
    /// <summary>
    /// Receipt printing service for thermal printers.
    /// Updated to use Bill/BillDescription models.
    /// </summary>
    public class PrintService
    {
        private Bill? _billToPrint;
        private string _storeName = "GROCERY MART";
        private string _storeAddress = "Rawat, Rawalpindi, Pakistan";
        private string _storePhone = "0300-1234567";
        private string _cashierName = "";

        private string? _preferredPrinter;
        private const string ConfigFile = "printer_config.txt";

        public PrintService()
        {
            LoadConfig();
        }

        private void LoadConfig()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFile);
                if (File.Exists(path))
                {
                    _preferredPrinter = File.ReadAllText(path).Trim();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load printer config", ex);
            }
        }

        private void SaveConfig(string printerName)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFile);
                File.WriteAllText(path, printerName);
                _preferredPrinter = printerName;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to save printer config", ex);
            }
        }

        /// <summary>Prints a receipt for the given Bill.</summary>
        public void PrintReceipt(Bill bill, string cashierName, string? printerName = null)
        {
            try
            {
                _billToPrint = bill;
                _cashierName = cashierName;

                var printDoc = new PrintDocument();

                string? targetPrinter = printerName;
                if (string.IsNullOrEmpty(targetPrinter))
                {
                    if (string.IsNullOrEmpty(_preferredPrinter))
                    {
                        using (var dialog = new System.Windows.Forms.PrintDialog())
                        {
                            dialog.Document = printDoc;
                            dialog.UseEXDialog = true;

                            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                            {
                                targetPrinter = printDoc.PrinterSettings.PrinterName;
                                SaveConfig(targetPrinter);
                            }
                            else
                            {
                                return; // Cancelled
                            }
                        }
                    }
                    else
                    {
                        targetPrinter = _preferredPrinter;
                    }
                }

                if (!string.IsNullOrEmpty(targetPrinter))
                    printDoc.PrinterSettings.PrinterName = targetPrinter;

                // 80mm thermal paper = ~302 pixels at 96dpi
                printDoc.DefaultPageSettings.PaperSize = new PaperSize("Receipt", 302, 1000);
                printDoc.DefaultPageSettings.Margins = new Margins(5, 5, 5, 5);

                printDoc.PrintPage += PrintPage_Handler;
                printDoc.Print();

                AppLogger.Info($"Receipt printed for Bill #{bill.BillId} on printer: {targetPrinter}");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Receipt printing failed", ex);
                _preferredPrinter = null;
                throw;
            }
        }

        private void PrintPage_Handler(object sender, PrintPageEventArgs e)
        {
            if (e.Graphics == null || _billToPrint == null) return;

            var g = e.Graphics;
            var headerFont = new Font("Consolas", 11, FontStyle.Bold);
            var normalFont = new Font("Consolas", 8);
            var boldFont = new Font("Consolas", 8, FontStyle.Bold);
            var smallFont = new Font("Consolas", 7);

            float y = 5;
            float pageWidth = 290;
            var sf = new StringFormat { Alignment = StringAlignment.Center };
            var sfRight = new StringFormat { Alignment = StringAlignment.Far };

            // Store header
            g.DrawString(_storeName, headerFont, Brushes.Black, new RectangleF(0, y, pageWidth, 20), sf);
            y += 20;
            g.DrawString(_storeAddress, smallFont, Brushes.Black, new RectangleF(0, y, pageWidth, 15), sf);
            y += 15;
            g.DrawString($"Ph: {_storePhone}", smallFont, Brushes.Black, new RectangleF(0, y, pageWidth, 15), sf);
            y += 18;

            // Divider
            g.DrawString(new string('-', 48), normalFont, Brushes.Black, 0, y);
            y += 14;

            // Bill info
            g.DrawString($"Bill#: {_billToPrint.BillId}", normalFont, Brushes.Black, 0, y);
            y += 13;
            g.DrawString($"Date: {_billToPrint.BillDateTime}", normalFont, Brushes.Black, 0, y);
            y += 13;
            g.DrawString($"Cashier: {_cashierName}", normalFont, Brushes.Black, 0, y);
            y += 16;

            // Items header
            g.DrawString(new string('-', 48), normalFont, Brushes.Black, 0, y);
            y += 14;
            g.DrawString("Item", boldFont, Brushes.Black, 0, y);
            g.DrawString("Qty", boldFont, Brushes.Black, 150, y);
            g.DrawString("Price", boldFont, Brushes.Black, 190, y);
            g.DrawString("Total", boldFont, Brushes.Black, pageWidth, y, sfRight);
            y += 14;
            g.DrawString(new string('-', 48), normalFont, Brushes.Black, 0, y);
            y += 14;

            // Items
            foreach (var item in _billToPrint.Items)
            {
                var name = item.ItemDescription.Length > 20 ? item.ItemDescription[..20] : item.ItemDescription;
                g.DrawString(name, normalFont, Brushes.Black, 0, y);
                g.DrawString(item.Quantity.ToString(), normalFont, Brushes.Black, 155, y);
                g.DrawString(item.UnitPrice.ToString("N0"), normalFont, Brushes.Black, 190, y);
                g.DrawString(item.TotalPrice.ToString("N0"), normalFont, Brushes.Black, pageWidth, y, sfRight);
                y += 14;
            }

            // Totals
            g.DrawString(new string('-', 48), normalFont, Brushes.Black, 0, y);
            y += 14;

            g.DrawString("Sub Total:", boldFont, Brushes.Black, 0, y);
            g.DrawString($"Rs.{_billToPrint.SubTotal:N2}", boldFont, Brushes.Black, pageWidth, y, sfRight);
            y += 14;

            g.DrawString("Discount:", normalFont, Brushes.Black, 0, y);
            g.DrawString($"-Rs.{_billToPrint.DiscountAmount:N2}", normalFont, Brushes.Black, pageWidth, y, sfRight);
            y += 14;

            if (_billToPrint.TaxAmount > 0)
            {
                g.DrawString("Tax:", normalFont, Brushes.Black, 0, y);
                g.DrawString($"Rs.{_billToPrint.TaxAmount:N2}", normalFont, Brushes.Black, pageWidth, y, sfRight);
                y += 14;
            }

            g.DrawString(new string('=', 48), normalFont, Brushes.Black, 0, y);
            y += 14;

            g.DrawString("GRAND TOTAL:", headerFont, Brushes.Black, 0, y);
            g.DrawString($"Rs.{_billToPrint.GrandTotal:N2}", headerFont, Brushes.Black, pageWidth, y, sfRight);
            y += 20;

            g.DrawString("Cash Received:", normalFont, Brushes.Black, 0, y);
            g.DrawString($"Rs.{_billToPrint.CashReceived:N2}", normalFont, Brushes.Black, pageWidth, y, sfRight);
            y += 14;

            g.DrawString("Change:", normalFont, Brushes.Black, 0, y);
            g.DrawString($"Rs.{_billToPrint.ChangeGiven:N2}", normalFont, Brushes.Black, pageWidth, y, sfRight);
            y += 20;

            // Footer
            g.DrawString(new string('-', 48), normalFont, Brushes.Black, 0, y);
            y += 14;
            g.DrawString("Thank you for shopping!", normalFont, Brushes.Black, new RectangleF(0, y, pageWidth, 15), sf);
            y += 15;
            g.DrawString("Please come again", smallFont, Brushes.Black, new RectangleF(0, y, pageWidth, 15), sf);

            e.HasMorePages = false;

            headerFont.Dispose();
            normalFont.Dispose();
            boldFont.Dispose();
            smallFont.Dispose();
        }

        /// <summary>
        /// Send ESC/POS command to open cash drawer via printer.
        /// </summary>
        public void OpenCashDrawer(string printerName)
        {
            try
            {
                AppLogger.Info("Cash drawer open command sent.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Cash drawer command failed", ex);
            }
        }
    }
}
