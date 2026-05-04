using SmartGridSuite.Client.Services;
using System.Windows;

namespace SmartGridSuite.Client
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            UiScaleService.Load();

            base.OnStartup(e);
        }
    }
}