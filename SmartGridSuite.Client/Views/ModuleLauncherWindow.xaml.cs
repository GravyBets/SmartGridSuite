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
            LoadUiScaleControl();
            UiScaleSlider.ValueChanged += UiScaleSlider_ValueChanged;
            ThemeToggle.IsChecked = ThemeService.Current == AppTheme.Dark;
        }

        private bool _loadingUiScale;

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

        private void LoadUiScaleControl()
        {
            _loadingUiScale = true;

            var percent = Math.Round(UiScaleService.CurrentScale * 100);

            UiScaleSlider.Value = percent;
            UiScaleValueTextBlock.Text = $"{percent:0}%";

            _loadingUiScale = false;
        }

        private void UiScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loadingUiScale)
                return;

            var percent = Math.Round(e.NewValue);
            var scale = percent / 100.0;

            UiScaleValueTextBlock.Text = $"{percent:0}%";
            UiScaleService.SaveScale(scale);
        }

        private void ResetUiScaleButton_Click(object sender, RoutedEventArgs e)
        {
            UiScaleService.SaveScale(0.80);
            LoadUiScaleControl();
        }
    }
}