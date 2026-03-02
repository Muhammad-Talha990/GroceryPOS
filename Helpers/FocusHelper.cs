using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GroceryPOS.Helpers
{
    public static class FocusHelper
    {
        public static readonly DependencyProperty SelectAllOnFocusProperty =
            DependencyProperty.RegisterAttached(
                "SelectAllOnFocus",
                typeof(bool),
                typeof(FocusHelper),
                new PropertyMetadata(false, OnSelectAllOnFocusChanged));

        public static bool GetSelectAllOnFocus(DependencyObject obj)
        {
            return (bool)obj.GetValue(SelectAllOnFocusProperty);
        }

        public static void SetSelectAllOnFocus(DependencyObject obj, bool value)
        {
            obj.SetValue(SelectAllOnFocusProperty, value);
        }

        private static void OnSelectAllOnFocusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBox textBox)
            {
                if ((bool)e.NewValue)
                {
                    textBox.GotKeyboardFocus += TextBox_GotKeyboardFocus;
                    textBox.PreviewMouseLeftButtonDown += TextBox_PreviewMouseLeftButtonDown;
                }
                else
                {
                    textBox.GotKeyboardFocus -= TextBox_GotKeyboardFocus;
                    textBox.PreviewMouseLeftButtonDown -= TextBox_PreviewMouseLeftButtonDown;
                }
            }
        }

        private static void TextBox_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.Dispatcher.BeginInvoke(new System.Action(() => textBox.SelectAll()));
            }
        }

        private static void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
            {
                textBox.Focus();
                e.Handled = true;
            }
        }

        // --- MoveFocusOnEnter Attached Property ---
        public static readonly DependencyProperty MoveFocusOnEnterProperty =
            DependencyProperty.RegisterAttached(
                "MoveFocusOnEnter",
                typeof(bool),
                typeof(FocusHelper),
                new PropertyMetadata(false, OnMoveFocusOnEnterChanged));

        public static bool GetMoveFocusOnEnter(DependencyObject obj) => (bool)obj.GetValue(MoveFocusOnEnterProperty);
        public static void SetMoveFocusOnEnter(DependencyObject obj, bool value) => obj.SetValue(MoveFocusOnEnterProperty, value);

        private static void OnMoveFocusOnEnterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UIElement element)
            {
                if ((bool)e.NewValue)
                    element.KeyDown += Element_KeyDown;
                else
                    element.KeyDown -= Element_KeyDown;
            }
        }

        private static void Element_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var element = sender as UIElement;
                element?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
        }

        // --- NumericOnly Attached Property ---
        public static readonly DependencyProperty NumericOnlyProperty =
            DependencyProperty.RegisterAttached(
                "NumericOnly",
                typeof(bool),
                typeof(FocusHelper),
                new PropertyMetadata(false, OnNumericOnlyChanged));

        public static bool GetNumericOnly(DependencyObject obj) => (bool)obj.GetValue(NumericOnlyProperty);
        public static void SetNumericOnly(DependencyObject obj, bool value) => obj.SetValue(NumericOnlyProperty, value);

        private static void OnNumericOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var enable = (bool)e.NewValue;
            if (d is TextBox textBox)
            {
                if (enable)
                {
                    textBox.PreviewTextInput += TextBox_PreviewTextInput;
                    DataObject.AddPastingHandler(textBox, OnPasting);
                }
                else
                {
                    textBox.PreviewTextInput -= TextBox_PreviewTextInput;
                    DataObject.RemovePastingHandler(textBox, OnPasting);
                }
            }
            else if (d is ComboBox comboBox)
            {
                if (comboBox.IsLoaded) SetupComboBoxRestriction(comboBox, enable, TextBox_PreviewTextInput, OnPasting);
                else comboBox.Loaded += (s, ev) => SetupComboBoxRestriction(comboBox, enable, TextBox_PreviewTextInput, OnPasting);
            }
        }

        private static void TextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Allow only digits
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private static void OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(DataFormats.Text))
            {
                var text = (string)e.DataObject.GetData(DataFormats.Text);
                if (text != null && !text.All(char.IsDigit))
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
        }

        // --- DecimalOnly Attached Property ---
        public static readonly DependencyProperty DecimalOnlyProperty =
            DependencyProperty.RegisterAttached(
                "DecimalOnly",
                typeof(bool),
                typeof(FocusHelper),
                new PropertyMetadata(false, OnDecimalOnlyChanged));

        public static bool GetDecimalOnly(DependencyObject obj) => (bool)obj.GetValue(DecimalOnlyProperty);
        public static void SetDecimalOnly(DependencyObject obj, bool value) => obj.SetValue(DecimalOnlyProperty, value);

        private static void OnDecimalOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var enable = (bool)e.NewValue;
            if (d is TextBox textBox)
            {
                if (enable)
                {
                    textBox.PreviewTextInput += TextBox_PreviewDecimalInput;
                    DataObject.AddPastingHandler(textBox, OnDecimalPasting);
                }
                else
                {
                    textBox.PreviewTextInput -= TextBox_PreviewDecimalInput;
                    DataObject.RemovePastingHandler(textBox, OnDecimalPasting);
                }
            }
            else if (d is ComboBox comboBox)
            {
                if (comboBox.IsLoaded) SetupComboBoxRestriction(comboBox, enable, TextBox_PreviewDecimalInput, OnDecimalPasting);
                else comboBox.Loaded += (s, ev) => SetupComboBoxRestriction(comboBox, enable, TextBox_PreviewDecimalInput, OnDecimalPasting);
            }
        }

        private static void SetupComboBoxRestriction(ComboBox comboBox, bool enable, TextCompositionEventHandler inputHandler, DataObjectPastingEventHandler pastingHandler)
        {
            // Find the internal TextBox of the ComboBox
            var textBox = comboBox.Template?.FindName("PART_EditableTextBox", comboBox) as TextBox;
            if (textBox != null)
            {
                if (enable)
                {
                    textBox.PreviewTextInput += inputHandler;
                    DataObject.AddPastingHandler(textBox, pastingHandler);
                }
                else
                {
                    textBox.PreviewTextInput -= inputHandler;
                    DataObject.RemovePastingHandler(textBox, pastingHandler);
                }
            }
        }

        private static void TextBox_PreviewDecimalInput(object sender, TextCompositionEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            // Allow digits
            if (e.Text.All(char.IsDigit)) return;

            // Allow a single decimal point
            if (e.Text == "." && !textBox.Text.Contains(".")) return;

            e.Handled = true;
        }

        private static void OnDecimalPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(DataFormats.Text))
            {
                var text = (string)e.DataObject.GetData(DataFormats.Text);
                if (text == null) { e.CancelCommand(); return; }

                var textBox = sender as TextBox;
                if (textBox == null) { e.CancelCommand(); return; }

                string currentText = textBox.Text;
                int selectionStart = textBox.SelectionStart;
                int selectionLength = textBox.SelectionLength;

                string newText = currentText.Remove(selectionStart, selectionLength).Insert(selectionStart, text);

                if (!IsValidDecimal(newText))
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
        }

        private static bool IsValidDecimal(string text)
        {
            if (string.IsNullOrEmpty(text) || text == ".") return true;
            return double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _);
        }

        // --- LettersOnly Attached Property ---
        public static readonly DependencyProperty LettersOnlyProperty =
            DependencyProperty.RegisterAttached(
                "LettersOnly",
                typeof(bool),
                typeof(FocusHelper),
                new PropertyMetadata(false, OnLettersOnlyChanged));

        public static bool GetLettersOnly(DependencyObject obj) => (bool)obj.GetValue(LettersOnlyProperty);
        public static void SetLettersOnly(DependencyObject obj, bool value) => obj.SetValue(LettersOnlyProperty, value);

        private static void OnLettersOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var enable = (bool)e.NewValue;
            if (d is TextBox textBox)
            {
                if (enable)
                {
                    textBox.PreviewTextInput += LettersOnly_PreviewTextInput;
                    DataObject.AddPastingHandler(textBox, LettersOnly_OnPasting);
                }
                else
                {
                    textBox.PreviewTextInput -= LettersOnly_PreviewTextInput;
                    DataObject.RemovePastingHandler(textBox, LettersOnly_OnPasting);
                }
            }
            else if (d is ComboBox comboBox)
            {
                if (comboBox.IsLoaded) SetupComboBoxRestriction(comboBox, enable, LettersOnly_PreviewTextInput, LettersOnly_OnPasting);
                else comboBox.Loaded += (s, ev) => SetupComboBoxRestriction(comboBox, enable, LettersOnly_PreviewTextInput, LettersOnly_OnPasting);
            }
        }

        private static void LettersOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Allow only letters and spaces
            e.Handled = !e.Text.All(c => char.IsLetter(c) || char.IsWhiteSpace(c));
        }

        private static void LettersOnly_OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(DataFormats.Text))
            {
                var text = (string)e.DataObject.GetData(DataFormats.Text);
                if (text != null && !text.All(c => char.IsLetter(c) || char.IsWhiteSpace(c)))
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
