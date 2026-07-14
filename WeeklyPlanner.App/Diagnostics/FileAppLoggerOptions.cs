namespace WeeklyPlanner.App.Diagnostics;

public sealed class FileAppLoggerOptions
{
    public string LogDirectoryPath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WeeklyPlanner",
        "Logs");

    public long MaximumFileSizeBytes { get; init; } = 5 * 1024 * 1024;

    public int RetentionDays { get; init; } = 14;

    public int QueueCapacity { get; init; } = 2048;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(LogDirectoryPath);

        if (!Path.IsPathFullyQualified(LogDirectoryPath))
        {
            throw new ArgumentException(
                "La cartella dei log deve essere un percorso assoluto.",
                nameof(LogDirectoryPath));
        }

        if (MaximumFileSizeBytes < 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumFileSizeBytes),
                "La dimensione massima del log deve essere almeno 1 KB.");
        }

        if (RetentionDays is < 1 or > 365)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RetentionDays),
                "La retention deve essere compresa tra 1 e 365 giorni.");
        }

        if (QueueCapacity is < 16 or > 100_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(QueueCapacity),
                "La coda deve contenere tra 16 e 100.000 elementi.");
        }
    }
}
