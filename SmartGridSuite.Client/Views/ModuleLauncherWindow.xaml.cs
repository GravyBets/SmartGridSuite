using SmartGridSuite.Client.Services;
using SmartGridSuite.Client.Views.FieldTechnician;
using SmartGridSuite.Client.Views.Administration;
using System;
using System.Threading.Tasks;
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

        private async void Tech_Click(object sender, RoutedEventArgs e)
        {
            if (!await CurrentUserHasRequiredRoleAsync("TECHNICIAN", "Field Technician"))
                return;

            var wnd = new FieldTechnicianShellWindow();
            wnd.Show();
            Close();
        }

        private async void Dispatch_Click(object sender, RoutedEventArgs e)
        {
            if (!await CurrentUserHasRequiredRoleAsync("DISPATCH", "Dispatcher"))
                return;

            var wnd = new DispatcherShellWindow();
            wnd.Show();
            Close();
        }

        private async void Admin_Click(object sender, RoutedEventArgs e)
        {
            if (!await CurrentUserHasRequiredRoleAsync("ADMIN", "Administration"))
                return;

            var win = new AdministrationShellWindow
            {
                Owner = this
            };

            win.Show();
            Hide();
        }

        private async Task<bool> CurrentUserHasRequiredRoleAsync(string requiredRoleCode, string moduleName)
        {
            try
            {
                var technician = await CurrentUserService.LoadCurrentTechnicianAsync(forceRefresh: true);

                if (technician == null)
                {
                    MessageBox.Show(
                        $"Access denied. Your Windows user '{CurrentUserService.CurrentEmployeeId}' was not found as an active technician.",
                        $"{moduleName} Access",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return false;
                }

                if (!CurrentUserService.HasRole(technician, requiredRoleCode))
                {
                    MessageBox.Show(
                        $"Access denied. Your Windows user '{CurrentUserService.CurrentEmployeeId}' does not have the {requiredRoleCode} role.",
                        $"{moduleName} Access",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to verify {moduleName} access.\n\n{ex.Message}",
                    $"{moduleName} Access",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return false;
            }
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