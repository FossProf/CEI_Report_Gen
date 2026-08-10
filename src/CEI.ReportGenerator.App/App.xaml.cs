using System.Windows;
using System.Net.Http;
using CEI.ReportGenerator.App.Services;
using CEI.ReportGenerator.Core.Services;
using CEI.ReportGenerator.App.Views;

namespace CEI.ReportGenerator.App;

public partial class App : Application
{
    public App()
    {
        SettingsStore = new ApplicationSettingsStore();
        Settings = SettingsStore.Load();
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        LocationResolver = new OpenMeteoProjectLocationResolver(httpClient);
        TemperatureService = new OpenMeteoProjectTemperatureService(httpClient);
        LocationResolutionWorkflow = new ProjectLocationResolutionWorkflow(LocationResolver);
    }

    public static App CurrentApp => (App)Current;

    public ApplicationSettingsStore SettingsStore { get; }

    public ApplicationSettings Settings { get; private set; }

    public IProjectLocationResolver LocationResolver { get; }

    public IProjectTemperatureService TemperatureService { get; }

    public ProjectLocationResolutionWorkflow LocationResolutionWorkflow { get; }

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
