using System;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace SmartGridSuite.Client.Views
{
    public partial class BugFeatureRequestWindow : Window
    {
        public BugFeatureRequestWindow()
        {
            InitializeComponent();
        }

        private void Window_ContentRendered(
            object? sender,
            EventArgs e)
        {
            RequestTitleTextBox.Focus();
        }

        private void Cancel_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }

        private void CopyRequest_Click(
            object sender,
            RoutedEventArgs e)
        {
            var requestType = GetSelectedText(
                RequestTypeComboBox,
                "Request");

            var area = GetSelectedText(
                AreaComboBox,
                "Other");

            var requestTitle =
                RequestTitleTextBox.Text.Trim();

            var details =
                DetailsTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(requestTitle))
            {
                MessageBox.Show(
                    this,
                    "Enter a short title for the request.",
                    "Bug / Feature Request",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                RequestTitleTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(details))
            {
                MessageBox.Show(
                    this,
                    "Enter the request details before copying it.",
                    "Bug / Feature Request",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                DetailsTextBox.Focus();
                return;
            }

            var requestText = BuildRequestText(
                requestType,
                area,
                requestTitle,
                details);

            try
            {
                Clipboard.SetText(requestText);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"The request could not be copied to the clipboard.\n\n{ex.Message}",
                    "Bug / Feature Request",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }

            MessageBox.Show(
                this,
                "Request copied to the clipboard.",
                "Bug / Feature Request",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Close();
        }

        private static string GetSelectedText(
            ComboBox comboBox,
            string fallback)
        {
            if (comboBox.SelectedItem is ComboBoxItem comboBoxItem)
            {
                return comboBoxItem.Content?.ToString()
                       ?? fallback;
            }

            return comboBox.SelectedItem?.ToString()
                   ?? fallback;
        }

        private static string BuildRequestText(
            string requestType,
            string area,
            string requestTitle,
            string details)
        {
            var builder = new StringBuilder();

            builder.AppendLine(
                "SMART GRID SUITE - BUG / FEATURE REQUEST");

            builder.AppendLine();

            builder.AppendLine(
                $"Type: {requestType}");

            builder.AppendLine(
                $"Title: {requestTitle}");

            builder.AppendLine(
                $"Area: {area}");

            builder.AppendLine(
                $"Submitted By: {Environment.UserName}");

            builder.AppendLine(
                $"Submitted At: {DateTime.Now:MM/dd/yyyy h:mm tt}");

            builder.AppendLine(
                $"Application Version: {GetApplicationVersion()}");

            builder.AppendLine();

            builder.AppendLine("DETAILS");
            builder.AppendLine("-------");
            builder.AppendLine(details);

            return builder.ToString();
        }

        private static string GetApplicationVersion()
        {
            var assembly =
                typeof(BugFeatureRequestWindow).Assembly;

            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                var metadataSeparator =
                    informationalVersion.IndexOf('+');

                return metadataSeparator >= 0
                    ? informationalVersion[..metadataSeparator]
                    : informationalVersion;
            }

            return assembly.GetName().Version?.ToString(3)
                   ?? "Development";
        }
    }
}