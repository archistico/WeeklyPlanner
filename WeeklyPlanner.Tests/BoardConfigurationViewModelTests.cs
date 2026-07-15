using WeeklyPlanner.App.ViewModels;
using WeeklyPlanner.Core.Models;
using WeeklyPlanner.Core.Repositories;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class BoardConfigurationViewModelTests
{
    [Fact]
    public async Task Selecting_priority_exposes_its_deadline_overrides()
    {
        var repository = StubCatalogRepository.CreateDefault();
        var viewModel = new BoardConfigurationViewModel(repository);

        await viewModel.LoadAsync();

        Assert.Equal(2, viewModel.Priorities.Count);
        Assert.Equal(3, viewModel.CardTypes.Count);
        Assert.Equal("U", Assert.IsType<PriorityCatalogItemViewModel>(viewModel.SelectedPriority).Code);

        viewModel.SelectedPriority = Assert.Single(
            viewModel.Priorities,
            item => item.Code == "D");

        Assert.Equal(3, viewModel.PriorityDeadlineRules.Count);
        var examRule = Assert.Single(
            viewModel.PriorityDeadlineRules,
            item => item.CardTypeName == "Esame strumentale");
        Assert.True(examRule.UseOverride);
        Assert.Equal(60, examRule.DueValue);
        Assert.Equal("giorni", examRule.SelectedUnit.DisplayName);
    }

    [Fact]
    public async Task New_priority_builds_a_normalized_save_request_with_rules()
    {
        var repository = StubCatalogRepository.CreateDefault();
        var viewModel = new BoardConfigurationViewModel(repository);
        await viewModel.LoadAsync();

        viewModel.NewPriorityCommand.Execute(null);
        viewModel.PriorityCode = "x";
        viewModel.PriorityName = "Straordinaria";
        viewModel.PriorityDueValue = 3;
        viewModel.PriorityDueUnit = Assert.Single(viewModel.DeadlineUnits, item => item.DisplayName == "giorni");
        var sqlRule = Assert.Single(viewModel.PriorityDeadlineRules, item => item.CardTypeName == "SQL");
        sqlRule.UseOverride = true;
        sqlRule.DueValue = 12;
        sqlRule.SelectedUnit = Assert.Single(sqlRule.DeadlineUnits, item => item.DisplayName == "ore");

        await viewModel.SavePriorityCommand.ExecuteAsync(null);

        var request = Assert.IsType<PrioritySaveRequest>(repository.LastPrioritySaveRequest);
        Assert.Null(request.Id);
        Assert.Equal(72, request.DefaultDueHours);
        var deadline = Assert.Single(request.DeadlineOverrides);
        Assert.Equal(sqlRule.CardTypeId, deadline.CardTypeId);
        Assert.Equal(12, deadline.DueHours);
        Assert.True(viewModel.ShowPrioritySuccess);
    }

    [Fact]
    public async Task Known_catalog_error_is_shown_without_losing_the_editor()
    {
        var repository = StubCatalogRepository.CreateDefault();
        var viewModel = new BoardConfigurationViewModel(repository);
        await viewModel.LoadAsync();
        repository.PrioritySaveException = new CardCatalogValidationException("Codice duplicato.");

        await viewModel.SavePriorityCommand.ExecuteAsync(null);

        Assert.True(viewModel.ShowPriorityError);
        Assert.Equal("Codice duplicato.", viewModel.PriorityMessage);
        Assert.True(viewModel.IsPriorityEditorVisible);
    }

    [Fact]
    public async Task Lane_save_uses_name_color_and_state_without_a_configurable_default()
    {
        var repository = StubCatalogRepository.CreateDefault();
        var viewModel = new BoardConfigurationViewModel(repository);
        await viewModel.LoadAsync();

        viewModel.NewCardTypeCommand.Execute(null);
        viewModel.CardTypeName = "Assistenza";
        viewModel.CardTypeColorHex = "#123456";

        await viewModel.SaveCardTypeCommand.ExecuteAsync(null);

        var request = Assert.IsType<CardTypeSaveRequest>(repository.LastCardTypeSaveRequest);
        Assert.Equal("Assistenza", request.Name);
        Assert.Equal("#123456", request.ColorHex);
        Assert.True(request.IsActive);
        Assert.True(viewModel.ShowCardTypeSuccess);
    }

    [Fact]
    public async Task Deleting_a_used_lane_requires_a_destination_and_passes_the_current_user()
    {
        var repository = StubCatalogRepository.CreateDefault();
        var viewModel = new BoardConfigurationViewModel(
            repository,
            userName: "  Emilie  ");
        await viewModel.LoadAsync();
        viewModel.SelectedCardType = Assert.Single(viewModel.CardTypes, item => item.Name == "SQL");

        viewModel.RequestDeleteCardTypeCommand.Execute(null);

        Assert.True(viewModel.IsCardTypeDeleteConfirmationVisible);
        Assert.True(viewModel.SelectedCardTypeHasCards);
        Assert.Contains("2 card", viewModel.CardTypeDeleteDescription, StringComparison.Ordinal);
        var destination = Assert.IsType<CardTypeCatalogItemViewModel>(
            viewModel.SelectedCardTypeDeleteDestination);
        Assert.Equal(SystemCardTypeKeys.Generic, destination.SystemKey);
        Assert.True(viewModel.CanConfirmDeleteCardType);

        await viewModel.ConfirmDeleteCardTypeCommand.ExecuteAsync(null);

        var request = Assert.IsType<CardTypeDeleteRequest>(repository.LastCardTypeDeleteRequest);
        Assert.Equal(2L, request.CardTypeId);
        Assert.Equal<long?>(destination.Id, request.DestinationCardTypeId);
        Assert.Equal<int?>(destination.Version, request.DestinationExpectedVersion);
        Assert.Equal("Emilie", request.UpdatedBy);
        Assert.True(viewModel.ShowCardTypeSuccess);
    }

    [Fact]
    public async Task Reordering_lanes_never_sends_generic_to_the_repository()
    {
        var repository = StubCatalogRepository.CreateDefault();
        var viewModel = new BoardConfigurationViewModel(repository);
        await viewModel.LoadAsync();
        viewModel.SelectedCardType = Assert.Single(
            viewModel.CardTypes,
            item => item.Name == "Esame strumentale");

        await viewModel.MoveCardTypeUpCommand.ExecuteAsync(null);

        Assert.Equal(new long[] { 3, 2 }, repository.LastCardTypeOrder!.Select(item => item.Id).ToArray());
        Assert.DoesNotContain(repository.LastCardTypeOrder!, item => item.Id == 1);
    }

    private sealed class StubCatalogRepository : ICardCatalogRepository
    {
        private CardCatalogSnapshot _snapshot;
        private long _nextPriorityId = 100;
        private long _nextCardTypeId = 100;

        public PrioritySaveRequest? LastPrioritySaveRequest { get; private set; }

        public CardTypeSaveRequest? LastCardTypeSaveRequest { get; private set; }

        public CardTypeDeleteRequest? LastCardTypeDeleteRequest { get; private set; }

        public IReadOnlyList<CatalogOrderItem>? LastCardTypeOrder { get; private set; }

        public Exception? PrioritySaveException { get; set; }

        private StubCatalogRepository(CardCatalogSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public static StubCatalogRepository CreateDefault()
        {
            var priorities = new[]
            {
                new PriorityDefinition
                {
                    Id = 1,
                    Code = "U",
                    Name = "Urgente",
                    DefaultDueHours = 72,
                    SortOrder = 0,
                    IsActive = true,
                    Version = 1,
                },
                new PriorityDefinition
                {
                    Id = 2,
                    Code = "D",
                    Name = "Differibile",
                    DefaultDueHours = 720,
                    SortOrder = 1,
                    IsActive = true,
                    Version = 1,
                },
            };
            var types = new[]
            {
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
                    Name = "SQL",
                    ColorHex = "#059669",
                    SortOrder = 1,
                    IsActive = true,
                    Version = 1,
                    CardCount = 2,
                },
                new CardTypeDefinition
                {
                    Id = 3,
                    Name = "Esame strumentale",
                    ColorHex = "#DC2626",
                    SortOrder = 2,
                    IsActive = true,
                    Version = 1,
                },
            };
            return new StubCatalogRepository(new CardCatalogSnapshot(
                priorities,
                types,
                [new PriorityTypeDeadline
                {
                    PriorityId = 2,
                    CardTypeId = 3,
                    DueHours = 1440,
                    Version = 1,
                }]));
        }

        public Task<CardCatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_snapshot);

        public Task<PriorityDefinition> SavePriorityAsync(
            PrioritySaveRequest request,
            CancellationToken cancellationToken = default)
        {
            LastPrioritySaveRequest = request;
            if (PrioritySaveException is not null)
            {
                return Task.FromException<PriorityDefinition>(PrioritySaveException);
            }

            var id = request.Id ?? _nextPriorityId++;
            var existing = _snapshot.Priorities.FirstOrDefault(item => item.Id == id);
            var saved = new PriorityDefinition
            {
                Id = id,
                Code = request.Code.Trim().ToUpperInvariant(),
                Name = request.Name.Trim(),
                Description = request.Description,
                DefaultDueHours = request.DefaultDueHours,
                SortOrder = existing?.SortOrder ?? _snapshot.Priorities.Count,
                IsActive = request.IsActive,
                IsDefault = request.IsDefault,
                Version = (existing?.Version ?? 0) + 1,
            };
            _snapshot = _snapshot with
            {
                Priorities = _snapshot.Priorities
                    .Where(item => item.Id != id)
                    .Append(saved)
                    .OrderBy(item => item.SortOrder)
                    .ToArray(),
                DeadlineRules = _snapshot.DeadlineRules.Where(item => item.PriorityId != id)
                    .Concat(request.DeadlineOverrides.Select(item => new PriorityTypeDeadline
                    {
                        PriorityId = id,
                        CardTypeId = item.CardTypeId,
                        DueHours = item.DueHours,
                        Version = 1,
                    }))
                    .ToArray(),
            };
            return Task.FromResult(saved);
        }

        public Task DeletePriorityAsync(
            long priorityId,
            int expectedVersion,
            CancellationToken cancellationToken = default)
        {
            _snapshot = _snapshot with
            {
                Priorities = _snapshot.Priorities.Where(item => item.Id != priorityId).ToArray(),
                DeadlineRules = _snapshot.DeadlineRules.Where(item => item.PriorityId != priorityId).ToArray(),
            };
            return Task.CompletedTask;
        }

        public Task ReorderPrioritiesAsync(
            IReadOnlyList<CatalogOrderItem> orderedItems,
            CancellationToken cancellationToken = default)
        {
            var byId = _snapshot.Priorities.ToDictionary(item => item.Id);
            _snapshot = _snapshot with
            {
                Priorities = orderedItems.Select((item, index) =>
                {
                    var current = byId[item.Id];
                    current.SortOrder = index;
                    current.Version++;
                    return current;
                }).ToArray(),
            };
            return Task.CompletedTask;
        }

        public Task<CardTypeDefinition> SaveCardTypeAsync(
            CardTypeSaveRequest request,
            CancellationToken cancellationToken = default)
        {
            LastCardTypeSaveRequest = request;
            var id = request.Id ?? _nextCardTypeId++;
            var existing = _snapshot.CardTypes.FirstOrDefault(item => item.Id == id);
            var saved = new CardTypeDefinition
            {
                Id = id,
                Name = request.Name.Trim(),
                ColorHex = request.ColorHex.Trim().ToUpperInvariant(),
                SortOrder = existing?.SortOrder ?? _snapshot.CardTypes.Count,
                IsActive = request.IsActive,
                IsDefault = existing?.IsDefault ?? false,
                Version = (existing?.Version ?? 0) + 1,
                SystemKey = existing?.SystemKey,
                IsSystem = existing?.IsSystem ?? false,
                CardCount = existing?.CardCount ?? 0,
            };
            _snapshot = _snapshot with
            {
                CardTypes = _snapshot.CardTypes
                    .Where(item => item.Id != id)
                    .Append(saved)
                    .OrderBy(item => item.SortOrder)
                    .ToArray(),
            };
            return Task.FromResult(saved);
        }

        public Task DeleteCardTypeAsync(
            CardTypeDeleteRequest request,
            CancellationToken cancellationToken = default)
        {
            LastCardTypeDeleteRequest = request;
            _snapshot = _snapshot with
            {
                CardTypes = _snapshot.CardTypes.Where(item => item.Id != request.CardTypeId).ToArray(),
                DeadlineRules = _snapshot.DeadlineRules
                    .Where(item => item.CardTypeId != request.CardTypeId)
                    .ToArray(),
            };
            return Task.CompletedTask;
        }

        public Task ReorderCardTypesAsync(
            IReadOnlyList<CatalogOrderItem> orderedItems,
            CancellationToken cancellationToken = default)
        {
            LastCardTypeOrder = orderedItems.ToArray();
            var generic = _snapshot.CardTypes.Single(item => item.IsSystem);
            var byId = _snapshot.CardTypes.Where(item => !item.IsSystem).ToDictionary(item => item.Id);
            _snapshot = _snapshot with
            {
                CardTypes = new[] { generic }
                    .Concat(orderedItems.Select((item, index) =>
                    {
                        var current = byId[item.Id];
                        current.SortOrder = index + 1;
                        current.Version++;
                        return current;
                    }))
                    .ToArray(),
            };
            return Task.CompletedTask;
        }
    }
}
