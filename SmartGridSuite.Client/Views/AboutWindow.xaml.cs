using System.Reflection;
using System.Windows;

namespace SmartGridSuite.Client.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();

            VersionTextBlock.Text =
                $"Version {GetApplicationVersion()}";
        }

        private void ChangeLogLink_Click(
            object sender,
            RoutedEventArgs e)
        {
            var window = new ChangeLogWindow
            {
                Owner = this
            };

            window.ShowDialog();
        }

        private void Close_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }

        private static string GetApplicationVersion()
        {
            var assembly = typeof(AboutWindow).Assembly;

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

            return assembly
                       .GetName()
                       .Version?
                       .ToString(3)
                   ?? "Development";
        }
    }
}