using SmartGridSuite.Client.Services;
using System.Windows.Controls;

namespace SmartGridSuite.Client.Views.Administration.WriteUpWorkflow
{
    public partial class WriteUpWorkflowAdminView : UserControl
    {
        private readonly ApiClient _api;

        public WriteUpWorkflowAdminView(
            ApiClient api)
        {
            InitializeComponent();

            _api = api;
        }
    }
}