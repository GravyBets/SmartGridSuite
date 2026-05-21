using SmartGridSuite.Contracts.SiteNotes;
using System.Windows;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteNoteEditorWindow : Window
    {
        // Keep this property so existing Add/Edit code still compiles.
        // We are no longer asking the dispatcher to choose a type.
        public string NoteType => "General";

        public string NoteText => NoteTextBox.Text.Trim();

        public SiteNoteEditorWindow(string siteId)
        {
            InitializeComponent();

            Title = "Add Site Note";
            HeaderTextBlock.Text = $"Add site note for {siteId}";

            Loaded += (_, _) =>
            {
                NoteTextBox.Focus();
            };
        }

        public SiteNoteEditorWindow(string siteId, SiteNoteDto existing)
        {
            InitializeComponent();

            Title = "Edit Site Note";
            HeaderTextBlock.Text = $"Edit site note for {siteId}";

            NoteTextBox.Text = existing.NoteText ?? "";

            Loaded += (_, _) =>
            {
                NoteTextBox.Focus();
                NoteTextBox.SelectAll();
            };
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NoteText))
            {
                MessageBox.Show(
                    "Note text is required.",
                    "Site Note",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                NoteTextBox.Focus();
                return;
            }

            DialogResult = true;
        }
    }
}