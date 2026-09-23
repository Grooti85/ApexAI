namespace ApexAI.Wpf;

public partial class App : System.Windows.Application
{
    private System.Threading.Mutex? _singleInstance;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        _singleInstance = new System.Threading.Mutex(true, "ApexAI.RaceEngineer", out var created);
        if (!created)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
