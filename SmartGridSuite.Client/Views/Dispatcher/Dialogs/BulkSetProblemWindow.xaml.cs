using System.Windows;

namespace SmartGridSuite.Client.Views.Dispatcher.Dialogs
{
    public partial class BulkSetProblemWindow : Window
    {
        public string Problem => ProblemTextBox.Text.Trim();

        public BulkSetProblemWindow(int ticketCount)
        {
            InitializeComponent();

            HeaderTextBlock.Text = $"Set Problem / Issue for {ticketCount} selected ticket(s)";
            Loaded += (_, _) =>
            {
                ProblemTextBox.Focus();
                ProblemTextBox.SelectAll();
            };
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Problem))
            {
                MessageBox.Show(
                    "Problem / Issue is required.",
                    "Set Problem",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                ProblemTextBox.Focus();
                return;
            }

            DialogResult = true;
        }
    }
}