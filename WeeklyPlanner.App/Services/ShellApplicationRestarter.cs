using System.Diagnostics;

namespace WeeklyPlanner.App.Services;

public sealed class ShellApplicationRestarter : IApplicationRestarter
{
    public bool TryStartNewInstance(out string? errorMessage)
    {
        errorMessage = null;
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            errorMessage = "Non è stato possibile determinare il percorso dell'applicazione.";
            return false;
        }

        try
        {
            var commandLineArguments = Environment.GetCommandLineArgs();
            var startInfo = new ProcessStartInfo
            {
                FileName = processPath,
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory,
            };

            if (IsDotnetHost(processPath) &&
                commandLineArguments.Length > 0 &&
                commandLineArguments[0].EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.ArgumentList.Add(commandLineArguments[0]);
            }

            foreach (var argument in commandLineArguments.Skip(1))
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = Process.Start(startInfo);
            if (process is null)
            {
                errorMessage = "Il sistema operativo non ha avviato la nuova istanza.";
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static bool IsDotnetHost(string processPath) =>
        string.Equals(
            Path.GetFileNameWithoutExtension(processPath),
            "dotnet",
            StringComparison.OrdinalIgnoreCase);
}
