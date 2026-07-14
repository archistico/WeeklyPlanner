using Avalonia;

namespace WeeklyPlanner.App;

internal static class Program
{
    // Il punto di ingresso deve restare privo di codice che tocchi tipi Avalonia prima di
    // AppMain, altrimenti si perde il supporto ai designer/anteprima XAML.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
