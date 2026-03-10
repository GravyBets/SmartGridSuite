using System.Windows;
using SmartGridSuite.Client.Services;

namespace SmartGridSuite.Client
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.Toggle();
        }
    }
}