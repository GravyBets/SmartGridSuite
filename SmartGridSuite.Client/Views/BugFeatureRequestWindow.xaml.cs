using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Settings;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmartGridSuite.Client.Views
{
    public partial class BugFeatureRequestWindow : Window
    {
        /*
         * This matches the API address currently used by the launcher.
         * Use your shared/configured API base URL here if that has since
         * been centralized.
         */
        private readonly ApiClient _api = ClientAppSettings.CreateApiClient();

        private bool _isSending;

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
            if (_isSending)
                return;

            Close();
        }

        private async void SendRequest_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_isSending)
                return;

            var requestType = GetSelectedText(
                RequestTypeComboBox,
                "Request");

            var applicationArea = GetSelectedText(
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
                    "Enter the request details before sending it.",
                    "Bug / Feature Request",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                DetailsTextBox.Focus();
                return;
            }

            SetSendingState(true);

            try
            {
                var response =
                    await _api.SubmitBugFeatureRequestAsync(
                        new SubmitBugFeatureRequest
                        {
                            RequestType = requestType,
                            ApplicationArea = applicationArea,
                            Title = requestTitle,
                            Details = details,
                            SubmittedBy = Environment.UserName,
                            ApplicationVersion =
                                GetApplicationVersion()
                        });

                if (response == null)
                {
                    MessageBox.Show(
                        this,
                        "The server returned an empty response.",
                        "Bug / Feature Request",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    return;
                }

                if (response.Sent)
                {
                    MessageBox.Show(
                        this,
                        "Your request was emailed successfully.",
                        "Bug / Feature Request",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    Close();
                    return;
                }

                if (response.Status.Equals(
                        "DryRun",
                        StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        this,
                        "The request was logged, but it was not emailed because Dry Run is enabled in General Settings.",
                        "Bug / Feature Request",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                if (response.Status.Equals(
                        "Skipped",
                        StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        this,
                        string.IsNullOrWhiteSpace(response.Message)
                            ? "The request was not emailed."
                            : response.Message,
                        "Bug / Feature Request",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                MessageBox.Show(
                    this,
                    string.IsNullOrWhiteSpace(response.Message)
                        ? "The request could not be emailed."
                        : response.Message,
                    "Bug / Feature Request",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (ApiClient.ApiConnectionException ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "Bug / Feature Request",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (ApiClient.ApiException ex)
            {
                MessageBox.Show(
                    this,
                    string.IsNullOrWhiteSpace(ex.Body)
                        ? "The server rejected the request."
                        : ex.Body,
                    "Bug / Feature Request",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"The request could not be sent.\n\n{ex.Message}",
                    "Bug / Feature Request",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                SetSendingState(false);
            }
        }

        private void SetSendingState(bool isSending)
        {
            _isSending = isSending;

            RequestTypeComboBox.IsEnabled = !isSending;
            AreaComboBox.IsEnabled = !isSending;
            RequestTitleTextBox.IsEnabled = !isSending;
            DetailsTextBox.IsEnabled = !isSending;
            CancelButton.IsEnabled = !isSending;
            SendRequestButton.IsEnabled = !isSending;

            SendRequestButton.Content =
                isSending
                    ? "Sending..."
                    : "Send Request";

            Mouse.OverrideCursor =
                isSending
                    ? Cursors.Wait
                    : null;
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

        private static string GetApplicationVersion()
        {
            var assembly =
                typeof(BugFeatureRequestWindow).Assembly;

            var informationalVersion = assembly
                .GetCustomAttribute<
                    AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrWhiteSpace(
                    informationalVersion))
            {
                var metadataSeparator =
                    informationalVersion.IndexOf('+');

                return metadataSeparator >= 0
                    ? informationalVersion[
                        ..metadataSeparator]
                    : informationalVersion;
            }

            return assembly
                       .GetName()
                       .Version?
                       .ToString(3)
                   ?? "Development";
        }
    }
}