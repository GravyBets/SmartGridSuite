using System.Windows;

namespace SmartGridSuite.Client.Views.Administration
{
    public partial class TruckStyleEditWindow : Window
    {
        public string StyleName { get; private set; } = "";

        public TruckStyleEditWindow()
        {
            InitializeComponent();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var name = StyleNameTextBox.Text.Trim();

            if (name.Length == 0)
            {
                MessageBox.Show("Style name is required.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StyleName = name;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}