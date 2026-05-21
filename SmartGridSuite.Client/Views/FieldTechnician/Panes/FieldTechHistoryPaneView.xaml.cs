using System.Windows;
using System.Windows.Controls;

namespace SmartGridSuite.Client.Views.FieldTechnician.Panes
{
    public partial class FieldTechHistoryPaneView : UserControl
    {
        public FieldTechHistoryPaneView()
        {
            InitializeComponent();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("History refresh wiring comes next.");
        }
    }
}