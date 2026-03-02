using System;
using System.Linq;
using System.Windows.Controls;

namespace GroceryPOS.Views
{
    public partial class SupplierBillsView : UserControl
    {
        public SupplierBillsView()
        {
            InitializeComponent();
        }

        private void SearchBox_GotFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // Use Dispatcher to ensure selection happens after mouse click processing
                textBox.Dispatcher.BeginInvoke(new Action(() => {
                    textBox.SelectAll();
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        private void Quantity_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            // Only allow digits
            e.Handled = !char.IsDigit(e.Text, e.Text.Length - 1);
        }

        private void Quantity_Pasting(object sender, System.Windows.DataObjectPastingEventArgs e)
        {
            // Only allow pasting numeric strings
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                string text = (string)e.DataObject.GetData(typeof(string));
                if (text.Any(c => !char.IsDigit(c))) // Simple digit check
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
        }
    }
}
