using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmartGridSuite.Client.Controls
{
    public static class TextBoxCommands
    {
        public static readonly RoutedUICommand ClearText = new(
            "Clear Text",
            "ClearText",
            typeof(TextBoxCommands));

        static TextBoxCommands()
        {
            CommandManager.RegisterClassCommandBinding(
                typeof(TextBox),
                new CommandBinding(
                    ClearText,
                    ExecuteClearText,
                    CanExecuteClearText));
        }

        private static void CanExecuteClearText(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = sender is TextBox textBox &&
                           !string.IsNullOrEmpty(textBox.Text);

            e.Handled = true;
        }

        private static void ExecuteClearText(object sender, ExecutedRoutedEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            textBox.Clear();
            textBox.Focus();

            e.Handled = true;
        }
    }
}