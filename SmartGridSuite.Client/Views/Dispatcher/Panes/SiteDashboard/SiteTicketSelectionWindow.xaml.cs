using SmartGridSuite.Contracts.Tickets;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteTicketSelectionWindow : Window
    {
        public SiteTicketResolutionCandidateDto? SelectedTicket
        {
            get;
            private set;
        }

        public SiteTicketSelectionWindow(
            IEnumerable<SiteTicketResolutionCandidateDto> candidates,
            string? message = null)
        {
            InitializeComponent();

            var rows =
                (candidates ??
                 Enumerable.Empty<SiteTicketResolutionCandidateDto>())
                .Where(x => x.TicketId > 0)
                .OrderBy(x => x.CreatedAt)
                .ThenBy(x => x.TicketId)
                .ToList();

            TicketsGrid.ItemsSource =
                rows;

            if (!string.IsNullOrWhiteSpace(message))
            {
                ExplanationTextBlock.Text =
                    message.Trim()
                    + System.Environment.NewLine
                    + System.Environment.NewLine
                    + "Select the ticket that should receive this write-up.";
            }

            if (rows.Count > 0)
            {
                TicketsGrid.SelectedIndex =
                    0;
            }
        }

        private void UseSelectedTicket_Click(
            object sender,
            RoutedEventArgs e)
        {
            ConfirmSelection();
        }

        private void TicketsGrid_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
        {
            if (TicketsGrid.SelectedItem is null)
                return;

            ConfirmSelection();
        }

        private void ConfirmSelection()
        {
            if (TicketsGrid.SelectedItem
                is not SiteTicketResolutionCandidateDto selected)
            {
                MessageBox.Show(
                    this,
                    "Select a ticket first.",
                    "Select Ticket",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            SelectedTicket =
                selected;

            DialogResult =
                true;
        }

        private void Cancel_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult =
                false;
        }
    }
}