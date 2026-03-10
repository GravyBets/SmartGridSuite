using SmartGridSuite.Client.Services;
using SmartGridSuite.Client.Views;
using SmartGridSuite.Client.Views.Administration;
using System.Windows;

namespace SmartGridSuite.Client.Views
{
    public partial class ModuleLauncherWindow : Window
    {
        public ModuleLauncherWindow()
        {
            InitializeComponent();
            ThemeToggle.IsChecked = ThemeService.Current == AppTheme.Dark;
        }

        private void ThemeToggle_Checked(object sender, RoutedEventArgs e)
            => ThemeService.Apply(AppTheme.Dark);

        private void ThemeToggle_Unchecked(object sender, RoutedEventArgs e)
            => ThemeService.Apply(AppTheme.Light);

        private void Tech_Click(object sender, RoutedEventArgs e)
            => MessageBox.Show("Tech module (next).");

        private void Dispatch_Click(object sender, RoutedEventArgs e)
        {
            var wnd = new DispatcherShellWindow();
            wnd.Show();
            Close();
        }

        private void Admin_Click(object sender, RoutedEventArgs e)
        {
            var win = new AdministrationShellWindow
            {
                Owner = this
            };

            win.Show();
            this.Hide(); // or Close() if you never want to return to launcher
        }

        private void Rma_Click(object sender, RoutedEventArgs e)
            => MessageBox.Show("RMA Testing (later).");

        private void About_Click(object sender, RoutedEventArgs e)
            => MessageBox.Show("About (later).");

        private void BugFeature_Click(object sender, RoutedEventArgs e)
            => MessageBox.Show("Bug/Feature link (later).");
    }
}