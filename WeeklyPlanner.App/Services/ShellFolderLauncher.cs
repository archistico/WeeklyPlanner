using System.Diagnostics;

namespace WeeklyPlanner.App.Services;

public sealed class ShellFolderLauncher : IFolderLauncher
{
    public void OpenFolder(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        Directory.CreateDirectory(folderPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = folderPath,
            UseShellExecute = true,
        });
    }
}
