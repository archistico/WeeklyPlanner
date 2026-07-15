using System.Diagnostics;
using WeeklyPlanner.Core.Models;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class KanbanScaleTests
{
    [Fact]
    [Trait("Category", "PerformanceSmoke")]
    public async Task Projection_handles_many_lanes_and_cards_within_the_smoke_budget()
    {
        const int laneCount = 30;
        const int cardsPerCell = 10;
        const int columnCount = 5;
        var context = BoardViewModelTestDoubles.Create();
        context.Snapshot.CardTypes = CreateCardTypes(laneCount);

        var cardId = 1L;
        for (var laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            var cardTypeId = laneIndex + 1L;
            for (var columnId = 0; columnId < columnCount; columnId++)
            {
                for (var index = 0; index < cardsPerCell; index++)
                {
                    context.Cards.Items.Add(new Card
                    {
                        Id = cardId,
                        ColumnId = columnId,
                        CardTypeId = cardTypeId,
                        StableId = $"scale-{cardId}",
                        CreatedAtUtc = context.Clock.Now.UtcDateTime.ToString("O"),
                        Title = $"Card {cardId}",
                        SortOrder = laneIndex * cardsPerCell + index,
                        CreatedBy = "Scale test",
                        UpdatedBy = "Scale test",
                        UpdatedAtUtc = context.Clock.Now.UtcDateTime.ToString("O"),
                        Version = 1,
                    });
                    cardId++;
                }
            }
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            await context.ViewModel.StartAsync();
            stopwatch.Stop();

            Assert.Equal(laneCount, context.ViewModel.Swimlanes.Count);
            Assert.Equal(
                laneCount * columnCount * cardsPerCell,
                context.ViewModel.Swimlanes.Sum(lane => lane.Cells.Sum(cell => cell.Cards.Count)));
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(10),
                $"La proiezione ha richiesto {stopwatch.Elapsed.TotalSeconds:F2} secondi.");
        }
        finally
        {
            await context.ViewModel.DisposeAsync();
        }
    }

    private static IReadOnlyList<CardTypeDefinition> CreateCardTypes(int laneCount)
    {
        var result = new List<CardTypeDefinition>(laneCount)
        {
            new()
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
        };

        for (var index = 1; index < laneCount; index++)
        {
            result.Add(new CardTypeDefinition
            {
                Id = index + 1,
                Name = $"Fascia {index:00}",
                ColorHex = index % 2 == 0 ? "#2563EB" : "#059669",
                SortOrder = index,
                IsActive = true,
                IsDefault = false,
                Version = 1,
            });
        }

        return result;
    }
}
