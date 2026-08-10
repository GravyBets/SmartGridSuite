using SmartGridSuite.Client.Services;
using System.Windows;
using System.Windows.Controls;

namespace SmartGridSuite.Client.Views.Administration.WriteUpWorkflow
{
    public partial class WriteUpWorkflowAdminView : UserControl
    {
        private readonly ApiClient _api;

        private bool _loading;

        public WriteUpWorkflowAdminView(
            ApiClient api)
        {
            InitializeComponent();

            _api = api;

            Loaded += WriteUpWorkflowAdminView_Loaded;
        }

        private async void WriteUpWorkflowAdminView_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            Loaded -= WriteUpWorkflowAdminView_Loaded;

            await LoadWriteUpFlagsAsync();
            await LoadReferToOptionsAsync();
            await LoadDispatchCloseoutChecklistDefinitionsAsync();
        }

        private void SetBusy(
            bool isBusy)
        {
            _loading = isBusy;

            UpdateWriteUpFlagBusyState(isBusy);
            UpdateReferToOptionBusyState(isBusy);
            UpdateDispatchCloseoutChecklistBusyState(isBusy);
        }
    }
}
