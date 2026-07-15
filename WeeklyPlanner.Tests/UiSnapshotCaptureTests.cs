using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WeeklyPlanner.App;
using WeeklyPlanner.App.Views;
using WeeklyPlanner.Core.Configuration;
using WeeklyPlanner.Core.Models;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class UiSnapshotCaptureTests
{
    [AvaloniaFact]
    [Trait("Category", "DocumentationCapture")]
    public async Task Capture_light_and_dark_board_images_when_explicitly_requested()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("WEEKLYPLANNER_CAPTURE_UI"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var context = BoardViewModelTestDoubles.Create();
        context.Snapshot.CardTypes =
        [
            new CardTypeDefinition
            {
                Id = 1,
                Name = "Generica",
                ColorHex = "#64748B",
                SortOrder = 0,
                IsActive = true,
                IsDefault = true,
                Version = 1,
                SystemKey = SystemCardTypeKeys.Generic,
                IsSystem = true,
            },
            new CardTypeDefinition
            {
                Id = 2,
                Name = "WinCliente",
                ColorHex = "#2563EB",
                SortOrder = 1,
                IsActive = true,
                Version = 1,
            },
            new CardTypeDefinition
            {
                Id = 3,
                Name = "SQL",
                ColorHex = "#059669",
                SortOrder = 2,
                IsActive = true,
                Version = 1,
            },
        ];
        context.Snapshot.Priorities =
        [
            new PriorityDefinition
            {
                Id = 1,
                Code = "U",
                Name = "Urgente",
                Description = "Entro 72 ore",
                DefaultDueHours = 72,
                SortOrder = 0,
                IsActive = true,
                Version = 1,
            },
        ];
        context.Cards.Items.AddRange(
        [
            CreateCard(1, 0, 1, "Valutare nuove richieste", priorityId: 1),
            CreateCard(2, 1, 2, "Preparare analisi funzionale"),
            CreateCard(3, 2, 2, "Implementare repository"),
            CreateCard(4, 3, 3, "Verificare query SQL"),
            CreateCard(5, 4, 1, "Rilascio completato"),
        ]);
        await context.ViewModel.StartAsync();

        try
        {
            var outputDirectory = GetScreenshotDirectory();
            Directory.CreateDirectory(outputDirectory);
            await CaptureAsync(
                context.ViewModel,
                AppThemePreference.Light,
                Path.Combine(outputDirectory, "board-light.png"));
            await CaptureAsync(
                context.ViewModel,
                AppThemePreference.Dark,
                Path.Combine(outputDirectory, "board-dark.png"));
        }
        finally
        {
            await context.ViewModel.DisposeAsync();
            if (Application.Current is global::WeeklyPlanner.App.App app)
            {
                app.ApplyThemePreference(AppThemePreference.System);
            }
        }
    }

    private static async Task CaptureAsync(
        object dataContext,
        AppThemePreference theme,
        string outputPath)
    {
        var app = Assert.IsType<global::WeeklyPlanner.App.App>(Application.Current);
        app.ApplyThemePreference(theme);
        var window = new MainWindow
        {
            DataContext = dataContext,
            Width = 1600,
            Height = 1000,
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Yield();

            using var frame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException(
                    "Avalonia Headless non ha prodotto un frame renderizzato.");
            frame.Save(outputPath);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static Card CreateCard(
        long id,
        long columnId,
        long cardTypeId,
        string title,
        long? priorityId = null) => new()
    {
        Id = id,
        ColumnId = columnId,
        CardTypeId = cardTypeId,
        PriorityId = priorityId,
        PriorityAssignedAtUtc = priorityId is null ? null : "2026-07-15T10:00:00.0000000Z",
        DueAtUtc = priorityId is null ? null : "2026-07-18T10:00:00.0000000Z",
        StableId = $"snapshot-{id}",
        CreatedAtUtc = "2026-07-15T10:00:00.0000000Z",
        Title = title,
        Notes = "Esempio per il manuale M3.14",
        SortOrder = 0,
        CreatedBy = "Emilie",
        UpdatedBy = "Emilie",
        UpdatedAtUtc = "2026-07-15T10:00:00.0000000Z",
        Version = 1,
    };

    private static string GetScreenshotDirectory() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "docs",
        "screenshots"));
}
