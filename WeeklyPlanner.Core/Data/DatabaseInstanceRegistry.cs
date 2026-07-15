using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WeeklyPlanner.Core.Data;

public sealed record DatabaseInstanceInfo(
    string DatabasePath,
    string SessionId,
    int ProcessId,
    DateTimeOffset RegisteredAtUtc,
    string MarkerPath);

public interface IDatabaseInstanceLease : IAsyncDisposable
{
    DatabaseInstanceInfo Instance { get; }
}

public interface IDatabaseInstanceRegistry
{
    IDatabaseInstanceLease Register(string databasePath, string sessionId);

    IReadOnlyList<DatabaseInstanceInfo> GetActiveInstances(
        string databasePath,
        string? excludedSessionId = null);
}

public sealed class DatabaseInstanceRegistry : IDatabaseInstanceRegistry
{
    private readonly string _rootDirectory;

    public DatabaseInstanceRegistry(string? rootDirectory = null)
    {
        _rootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WeeklyPlanner",
            "Sessions");

        if (!Path.IsPathFullyQualified(_rootDirectory))
        {
            throw new ArgumentException("La cartella delle sessioni deve essere un percorso assoluto.", nameof(rootDirectory));
        }
    }

    public IDatabaseInstanceLease Register(string databasePath, string sessionId)
    {
        var normalizedPath = NormalizeDatabasePath(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var directory = GetDatabaseDirectory(normalizedPath);
        Directory.CreateDirectory(directory);
        CleanupStaleMarkers(directory);

        var markerPath = Path.Combine(directory, $"{SanitizeFileName(sessionId)}-{Environment.ProcessId}.json");
        var instance = new DatabaseInstanceInfo(
            normalizedPath,
            sessionId,
            Environment.ProcessId,
            DateTimeOffset.UtcNow,
            markerPath);

        var payload = JsonSerializer.Serialize(instance);
        var tempPath = markerPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, payload, Encoding.UTF8);
            File.Move(tempPath, markerPath, overwrite: true);
        }
        finally
        {
            TryDelete(tempPath);
        }

        return new DatabaseInstanceLease(instance);
    }

    public IReadOnlyList<DatabaseInstanceInfo> GetActiveInstances(
        string databasePath,
        string? excludedSessionId = null)
    {
        var normalizedPath = NormalizeDatabasePath(databasePath);
        var directory = GetDatabaseDirectory(normalizedPath);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var active = new List<DatabaseInstanceInfo>();
        foreach (var markerPath in Directory.EnumerateFiles(directory, "*.json"))
        {
            DatabaseInstanceInfo? instance = null;
            try
            {
                instance = JsonSerializer.Deserialize<DatabaseInstanceInfo>(File.ReadAllText(markerPath));
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            if (instance is null ||
                !string.Equals(instance.DatabasePath, normalizedPath, PathComparison()) ||
                !IsProcessRunning(instance.ProcessId))
            {
                TryDelete(markerPath);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(excludedSessionId) &&
                string.Equals(instance.SessionId, excludedSessionId, StringComparison.Ordinal))
            {
                continue;
            }

            active.Add(instance with { MarkerPath = markerPath });
        }

        return active
            .OrderBy(item => item.RegisteredAtUtc)
            .ThenBy(item => item.SessionId, StringComparer.Ordinal)
            .ToArray();
    }

    private string GetDatabaseDirectory(string normalizedDatabasePath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedDatabasePath));
        return Path.Combine(_rootDirectory, Convert.ToHexString(hash.AsSpan(0, 12)));
    }

    private static string NormalizeDatabasePath(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        return Path.GetFullPath(databasePath.Trim());
    }

    private static void CleanupStaleMarkers(string directory)
    {
        foreach (var markerPath in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var instance = JsonSerializer.Deserialize<DatabaseInstanceInfo>(File.ReadAllText(markerPath));
                if (instance is null || !IsProcessRunning(instance.ProcessId))
                {
                    TryDelete(markerPath);
                }
            }
            catch (JsonException)
            {
                TryDelete(markerPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        if (processId <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var normalized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "session" : normalized;
    }

    private static StringComparison PathComparison() => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class DatabaseInstanceLease(DatabaseInstanceInfo instance) : IDatabaseInstanceLease
    {
        private int _disposed;

        public DatabaseInstanceInfo Instance { get; } = instance;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                TryDelete(Instance.MarkerPath);
            }

            return ValueTask.CompletedTask;
        }
    }
}
