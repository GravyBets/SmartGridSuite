using SmartGridSuite.Client.Services;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace SmartGridSuite.Client.Views
{
    public partial class ChangeLogWindow : Window
    {
        public ChangeLogWindow()
        {
            InitializeComponent();

            InterfaceScaleService.SetIsEnabled(
                this,
                false);

            Loaded += ChangeLogWindow_Loaded;
        }

        private void ChangeLogWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                ChangeLogItemsControl.ItemsSource =
                    LoadChangeLog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to load the change log.\n\n{ex.Message}",
                    "Change Log",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private static IReadOnlyList<ChangeLogEntry> LoadChangeLog()
        {
            var resourceUri =
                new Uri(
                    "pack://application:,,,/Assets/Data/ChangeLog.json",
                    UriKind.Absolute);

            var resource =
                Application.GetResourceStream(resourceUri);

            if (resource == null)
            {
                throw new FileNotFoundException(
                    "The embedded change-log file was not found.");
            }

            using var reader =
                new StreamReader(resource.Stream);

            var json =
                reader.ReadToEnd();

            return JsonSerializer.Deserialize<List<ChangeLogEntry>>(
                       json,
                       new JsonSerializerOptions
                       {
                           PropertyNameCaseInsensitive = true
                       })
                   ?? [];
        }

        private void Close_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }

        private sealed class ChangeLogEntry
        {
            public string Version { get; set; } = "";

            public string Date { get; set; } = "";

            public string Title { get; set; } = "";

            public List<string> Changes { get; set; } = [];
        }
    }
}