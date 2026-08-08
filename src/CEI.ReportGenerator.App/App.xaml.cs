using System.Windows;
using CEI.ReportGenerator.App.Views;

namespace CEI.ReportGenerator.App;

public partial class App : Application
{
    public App()
    {
        SettingsStore = new ApplicationSettingsStore();
        Settings = SettingsStore.Load();
    }

    public static App CurrentApp => (App)Current;

    public ApplicationSettingsStore SettingsStore { get; }

    public ApplicationSettings Settings { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    public void ApplySettings(ApplicationSettings settings)
    {
        Settings = settings;
    }
}
