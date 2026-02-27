using System.Windows;
using System.Windows.Controls;

namespace GroceryPOS.Views
{
    public partial class BillingView : UserControl
    {
        public BillingView()
        {
            InitializeComponent();
            Loaded += (s, e) => BarcodeTextBox.Focus();
        }
    }
}
