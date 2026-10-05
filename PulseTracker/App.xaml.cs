using System.Windows;
using PulseTracker.Services;

namespace PulseTracker
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ThemeService.Initialize();
            Database.Initialize();

            var login = new LoginWindow();
            MainWindow = login;
            login.Show();
        }
    }
}
