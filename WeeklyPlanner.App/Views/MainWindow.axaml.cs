using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WeeklyPlanner.App.Interaction;
using WeeklyPlanner.App.Services;
using WeeklyPlanner.App.ViewModels;
using WeeklyPlanner.Core.Configuration;
using WeeklyPlanner.Core.Data;

namespace WeeklyPlanner.App.Views;

public partial class MainWindow : Window
{
    private static readonly DataFormat<string> CardDragFormat =
        DataFormat.CreateStringApplicationFormat("weeklyplanner-card");
    private static readonly Cursor BoardPanCursor = new(StandardCursorType.SizeAll);
    private const double DragThreshold = 5;

    private CardViewModel? _pendingDragCard;
    private Point _dragStartPoint;
    private bool _dragInProgress;
    private bool _shutdownInProgress;
    private bool _shutdownCompleted;
    private CardViewModel? _dropIndicatorCard;
    private SwimlaneCellViewModel? _dropIndicatorCell;
    private bool _boardPanInProgress;
    private Point _boardPanStartPoint;
    private Vector _boardPanStartOffset;
    private IViewModelFactory? _viewModelFactory;
    private IApplicationRestarter? _applicationRestarter;
    private AppSettings? _applicationSettings;

    public MainWindow()
    {
        InitializeComponent();

        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDragLeaveHandler(this, OnDragLeave);
        DragDrop.AddDropHandler(this, OnDrop);

        BoardScrollViewer.AddHandler(
            InputElement.PointerPressedEvent,
            OnBoardPanPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        BoardScrollViewer.AddHandler(
            InputElement.PointerMovedEvent,
            OnBoardPanPointerMoved,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        BoardScrollViewer.AddHandler(
            InputElement.PointerReleasedEvent,
            OnBoardPanPointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    public void ConfigureApplicationServices(
        IViewModelFactory viewModelFactory,
        AppSettings settings,
        IApplicationRestarter applicationRestarter)
    {
        ArgumentNullException.ThrowIfNull(viewModelFactory);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(applicationRestarter);

        _viewModelFactory = viewModelFactory;
        _applicationRestarter = applicationRestarter;
        _applicationSettings = settings.Clone();
        _applicationSettings.Normalize();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (_shutdownCompleted || e.Cancel || DataContext is not IAsyncDisposable disposable)
        {
            return;
        }

        e.Cancel = true;
        if (_shutdownInProgress)
        {
            return;
        }

        _shutdownInProgress = true;
        IsEnabled = false;
        _ = CompleteShutdownAsync(disposable);
    }

    private async Task CompleteShutdownAsync(IAsyncDisposable disposable)
    {
        try
        {
            await disposable.DisposeAsync();
        }
        catch
        {
            // Il cleanup è best effort: i lease residui scadranno automaticamente.
        }
        finally
        {
            _shutdownCompleted = true;

            // Non affidarsi soltanto alla rilevazione implicita dell'ultima finestra chiusa:
            // dopo il cleanup della board richiedere esplicitamente la terminazione del lifetime.
            if (Application.Current?.ApplicationLifetime is
                IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
            else
            {
                Close();
            }
        }
    }

    private async void OnOpenDatabaseSafetyClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModelFactory is null ||
            _applicationRestarter is null ||
            _applicationSettings is null ||
            DataContext is not BoardViewModel boardViewModel)
        {
            return;
        }

        if (!boardViewModel.CanChangeIdentityAndDatabaseSettings)
        {
            var blockedWindow = new DatabaseRestoreResultWindow(
                new DatabaseRestoreStartupResult(
                    DatabaseRestoreStartupStatus.Blocked,
                    "Termina la modifica delle card e attendi la conclusione delle operazioni prima di aprire backup e ripristino."));
            await blockedWindow.ShowDialog(this);
            return;
        }

        var viewModel = _viewModelFactory.CreateDatabaseSafetyViewModel(_applicationSettings);
        var window = new DatabaseSafetyWindow
        {
            DataContext = viewModel,
        };

        viewModel.RestorePrepared += (_, preparation) => window.Close(preparation);
        var preparation = await window.ShowDialog<DatabaseRestorePreparation?>(this);
        if (preparation is null)
        {
            return;
        }

        if (!_applicationRestarter.TryStartNewInstance(out var restartError))
        {
            await viewModel.CancelPreparedRestoreAsync(preparation);
            var errorWindow = new DatabaseRestoreResultWindow(
                new DatabaseRestoreStartupResult(
                    DatabaseRestoreStartupStatus.Failed,
                    $"Il ripristino è stato annullato perché non è stato possibile riavviare WeeklyPlanner. Dettagli: {restartError}"));
            await errorWindow.ShowDialog(this);
            return;
        }

        _shutdownInProgress = true;
        IsEnabled = false;
        try
        {
            await boardViewModel.DisposeAsync();
        }
        finally
        {
            _shutdownCompleted = true;
            if (Application.Current?.ApplicationLifetime is
                IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
            else
            {
                Close();
            }
        }
    }

    private async void OnOpenDiagnosticsClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModelFactory is null ||
            _applicationSettings is null ||
            DataContext is not BoardViewModel boardViewModel)
        {
            return;
        }

        var viewModel = _viewModelFactory.CreateDiagnosticsViewModel(
            _applicationSettings,
            boardViewModel.GetRuntimeDiagnostics());
        var window = new DiagnosticsWindow
        {
            DataContext = viewModel,
        };

        await window.ShowDialog(this);
    }

    private async void OnOpenCardInformationClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: CardViewModel card } ||
            DataContext is not BoardViewModel boardViewModel)
        {
            return;
        }

        var window = new CardInformationWindow
        {
            DataContext = boardViewModel.CreateCardInformationViewModel(card),
        };
        await window.ShowDialog(this);
    }

    private async void OnOpenSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModelFactory is null ||
            _applicationSettings is null ||
            DataContext is not BoardViewModel boardViewModel)
        {
            return;
        }

        var viewModel = _viewModelFactory.CreateSettingsViewModel(
            _applicationSettings,
            boardViewModel.CanChangeIdentityAndDatabaseSettings);
        var window = new SettingsWindow
        {
            DataContext = viewModel,
            BoardConfigurationViewModel = _viewModelFactory.CreateBoardConfigurationViewModel(
                boardViewModel.CurrentDatabasePath,
                boardViewModel.CurrentUserName),
        };

        viewModel.Completed += (_, result) => window.Close(result);
        var result = await window.ShowDialog<SettingsSaveResult?>(this);
        if (result is null)
        {
            return;
        }

        _applicationSettings = result.Settings.Clone();
        if (Application.Current is App app)
        {
            app.ApplyThemePreference(_applicationSettings.ThemePreference);
        }

        boardViewModel.ApplyRuntimeSettings(
            _applicationSettings,
            result.RequiresRestart);
    }

    private async void OnCardFieldGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (sender is not Control control ||
            control.DataContext is not CardViewModel card ||
            DataContext is not BoardViewModel boardViewModel ||
            card.IsEditing ||
            card.IsLockedByAnotherUser)
        {
            return;
        }

        if (!await boardViewModel.BeginEditCardAsync(card))
        {
            FindAncestorBorderWithClass(control, "card")?.Focus();
        }
    }

    private async void OnPrioritySummaryPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control ||
            control.DataContext is not CardViewModel card ||
            DataContext is not BoardViewModel boardViewModel ||
            card.IsEditing ||
            card.IsLockedByAnotherUser ||
            !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Handled = true;
        await OpenPriorityEditorAsync(control, card, boardViewModel);
    }

    private async void OnPrioritySummaryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) ||
            sender is not Control control ||
            control.DataContext is not CardViewModel card ||
            DataContext is not BoardViewModel boardViewModel ||
            card.IsEditing ||
            card.IsLockedByAnotherUser)
        {
            return;
        }

        e.Handled = true;
        await OpenPriorityEditorAsync(control, card, boardViewModel);
    }

    private static async Task OpenPriorityEditorAsync(
        Control control,
        CardViewModel card,
        BoardViewModel boardViewModel)
    {
        var cardBorder = FindAncestorBorderWithClass(control, "card");
        if (!await boardViewModel.BeginEditCardAsync(card))
        {
            cardBorder?.Focus();
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                var priorityCombo = cardBorder?
                    .GetVisualDescendants()
                    .OfType<ComboBox>()
                    .FirstOrDefault(comboBox => ReferenceEquals(comboBox.DataContext, card));
                if (priorityCombo is null)
                {
                    return;
                }

                priorityCombo.Focus();
                priorityCombo.IsDropDownOpen = true;
            },
            DispatcherPriority.Background);
    }

    private async void OnCardFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape ||
            sender is not Control control ||
            control.DataContext is not CardViewModel card ||
            DataContext is not BoardViewModel boardViewModel)
        {
            return;
        }

        e.Handled = true;
        await boardViewModel.CancelEditCommand.ExecuteAsync(card);
        FindAncestorBorderWithClass(control, "card")?.Focus();
    }

    private async void OnCardKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Border cardBorder ||
            !ReferenceEquals(e.Source, cardBorder) ||
            cardBorder.DataContext is not CardViewModel card ||
            DataContext is not BoardViewModel { CanModifyBoard: true } boardViewModel ||
            !card.CanDrag)
        {
            return;
        }

        var sourceCell = FindSwimlaneCell(boardViewModel, card);
        var sourceLane = sourceCell is null
            ? null
            : boardViewModel.Swimlanes.FirstOrDefault(
                lane => lane.Cells.Any(cell => ReferenceEquals(cell, sourceCell)));
        if (sourceCell is null || sourceLane is null)
        {
            return;
        }

        SwimlaneCellViewModel? targetCell = null;
        var targetCellIndex = 0;
        if (e.KeyModifiers == KeyModifiers.Alt &&
            TryGetMoveDirection(e.Key, out var direction))
        {
            var sourceCellIndex = IndexOfCell(sourceLane.Cells, sourceCell);
            var sourceCardIndex = sourceCell.Cards.IndexOf(card);
            var cardCounts = sourceLane.Cells.Select(cell => cell.Cards.Count).ToArray();
            if (!CardMovePlanner.TryCreate(
                    sourceCellIndex,
                    sourceCardIndex,
                    cardCounts,
                    direction,
                    out var plan))
            {
                return;
            }

            targetCell = sourceLane.Cells[plan.TargetColumnIndex];
            targetCellIndex = plan.TargetIndex;
        }
        else if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt) &&
                 TryGetLaneDelta(e.Key, out var laneDelta))
        {
            var sourceLaneIndex = IndexOfLane(boardViewModel.Swimlanes, sourceLane);
            var targetLane = FindActiveLane(
                boardViewModel.Swimlanes,
                sourceLaneIndex,
                laneDelta);
            if (targetLane is null)
            {
                return;
            }

            targetCell = targetLane.GetCell(sourceCell.ColumnId);
            targetCellIndex = Math.Min(
                sourceCell.Cards.IndexOf(card),
                targetCell.Cards.Count);
        }

        if (targetCell is null)
        {
            return;
        }

        e.Handled = true;
        await boardViewModel.MoveCardAsync(card, targetCell, targetCellIndex);

        Dispatcher.UIThread.Post(
            () => FindCardBorder(card)?.Focus(),
            DispatcherPriority.Background);
    }

    private void OnBoardPanPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_boardPanInProgress || sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        var pointerPoint = e.GetCurrentPoint(scrollViewer);
        if (!pointerPoint.Properties.IsMiddleButtonPressed)
        {
            return;
        }

        _boardPanInProgress = true;
        _boardPanStartPoint = e.GetPosition(scrollViewer);
        _boardPanStartOffset = scrollViewer.Offset;
        scrollViewer.Cursor = BoardPanCursor;
        e.Pointer.Capture(scrollViewer);
        e.Handled = true;
    }

    private void OnBoardPanPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_boardPanInProgress || sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        if (!e.GetCurrentPoint(scrollViewer).Properties.IsMiddleButtonPressed)
        {
            EndBoardPan(e.Pointer);
            return;
        }

        scrollViewer.Offset = BoardPanCalculator.CalculateOffset(
            _boardPanStartOffset,
            _boardPanStartPoint,
            e.GetPosition(scrollViewer));
        e.Handled = true;
    }

    private void OnBoardPanPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_boardPanInProgress)
        {
            return;
        }

        EndBoardPan(e.Pointer);
        e.Handled = true;
    }

    private void EndBoardPan(IPointer pointer)
    {
        _boardPanInProgress = false;
        BoardScrollViewer.Cursor = Cursor.Default;
        pointer.Capture(null);
    }

    private void OnCardDragHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_dragInProgress || sender is not Control handle)
        {
            return;
        }

        var point = e.GetCurrentPoint(handle);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var cardBorder = FindAncestorBorderWithClass(handle, "card");
        if (cardBorder?.DataContext is not CardViewModel card || !card.CanDrag)
        {
            return;
        }

        _pendingDragCard = card;
        _dragStartPoint = e.GetPosition(this);
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private async void OnCardDragHandlePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragInProgress || _pendingDragCard is null)
        {
            return;
        }

        var currentPosition = e.GetPosition(this);
        var horizontalDistance = Math.Abs(currentPosition.X - _dragStartPoint.X);
        var verticalDistance = Math.Abs(currentPosition.Y - _dragStartPoint.Y);

        if (horizontalDistance < DragThreshold && verticalDistance < DragThreshold)
        {
            return;
        }

        var card = _pendingDragCard;
        _pendingDragCard = null;
        _dragInProgress = true;
        e.Pointer.Capture(null);

        try
        {
            var dataTransfer = new DataTransfer();
            dataTransfer.Add(DataTransferItem.Create(
                CardDragFormat,
                card.Model.Id.ToString(CultureInfo.InvariantCulture)));
            await DragDrop.DoDragDropAsync(e, dataTransfer, DragDropEffects.Move);
        }
        finally
        {
            _dragInProgress = false;
            ClearDropIndicators();
        }
    }

    private void OnCardDragHandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pendingDragCard = null;
        e.Pointer.Capture(null);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (DataContext is not BoardViewModel { CanModifyBoard: true } boardViewModel ||
            e.Source is not Control targetControl ||
            !TryGetDraggedCard(e, boardViewModel, out var card) ||
            !TryGetDropTarget(e, targetControl, boardViewModel, card, out var dropTarget) ||
            !WouldChangePosition(boardViewModel, card, dropTarget))
        {
            ClearDropIndicators();
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        SetDropIndicator(dropTarget);
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        ClearDropIndicators();
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        try
        {
            if (DataContext is not BoardViewModel { CanModifyBoard: true } boardViewModel ||
                e.Source is not Control targetControl ||
                !TryGetDraggedCard(e, boardViewModel, out var card) ||
                !TryGetDropTarget(e, targetControl, boardViewModel, card, out var dropTarget) ||
                !WouldChangePosition(boardViewModel, card, dropTarget))
            {
                e.DragEffects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
            await boardViewModel.MoveCardAsync(
                card,
                dropTarget.Cell,
                dropTarget.TargetCellIndex);
        }
        finally
        {
            ClearDropIndicators();
        }
    }

    private static bool TryGetDraggedCard(
        DragEventArgs e,
        BoardViewModel boardViewModel,
        out CardViewModel card)
    {
        var cardIdText = e.DataTransfer.TryGetValue(CardDragFormat);
        if (long.TryParse(
                cardIdText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var cardId))
        {
            var foundCard = boardViewModel.Columns
                .SelectMany(column => column.Cards)
                .FirstOrDefault(candidate => candidate.Model.Id == cardId);
            if (foundCard is not null && foundCard.CanDrag)
            {
                card = foundCard;
                return true;
            }
        }

        card = null!;
        return false;
    }

    private static bool TryGetDropTarget(
        DragEventArgs e,
        Control targetControl,
        BoardViewModel boardViewModel,
        CardViewModel draggedCard,
        out DropTarget dropTarget)
    {
        var cellBorder = FindAncestorBorderWithClass(targetControl, "swimlaneCell");
        if (cellBorder?.DataContext is not SwimlaneCellViewModel targetCell)
        {
            dropTarget = default;
            return false;
        }

        var sourceCell = FindSwimlaneCell(boardViewModel, draggedCard);
        if (sourceCell is null ||
            (!targetCell.IsCardTypeActive &&
             sourceCell.CardTypeId != targetCell.CardTypeId))
        {
            dropTarget = default;
            return false;
        }

        var cardBorder = FindAncestorBorderWithClass(targetControl, "card");
        if (cardBorder?.DataContext is not CardViewModel targetCard ||
            !targetCell.Cards.Contains(targetCard))
        {
            dropTarget = new DropTarget(
                targetCell,
                targetCell.Cards.Count,
                TargetCard: null,
                AfterCard: false);
            return true;
        }

        var targetCardIndex = targetCell.Cards.IndexOf(targetCard);
        var afterCard = e.GetPosition(cardBorder).Y >= cardBorder.Bounds.Height / 2;
        dropTarget = new DropTarget(
            targetCell,
            afterCard ? targetCardIndex + 1 : targetCardIndex,
            targetCard,
            afterCard);
        return true;
    }

    private static bool WouldChangePosition(
        BoardViewModel boardViewModel,
        CardViewModel card,
        DropTarget dropTarget)
    {
        var sourceCell = FindSwimlaneCell(boardViewModel, card);
        if (sourceCell is null)
        {
            return false;
        }

        if (sourceCell.CardTypeId != dropTarget.Cell.CardTypeId ||
            sourceCell.ColumnId != dropTarget.Cell.ColumnId)
        {
            return true;
        }

        return CardMovePlanner.WouldChangePosition(
            sourceColumnIndex: 0,
            sourceCardIndex: sourceCell.Cards.IndexOf(card),
            sourceColumnCount: sourceCell.Cards.Count,
            targetColumnIndex: 0,
            targetIndex: dropTarget.TargetCellIndex);
    }

    private void SetDropIndicator(DropTarget dropTarget)
    {
        if (ReferenceEquals(_dropIndicatorCard, dropTarget.TargetCard) &&
            ReferenceEquals(
                _dropIndicatorCell,
                dropTarget.TargetCard is null ? dropTarget.Cell : null))
        {
            if (dropTarget.TargetCard is not null)
            {
                dropTarget.TargetCard.SetDropIndicator(dropTarget.AfterCard);
            }

            return;
        }

        ClearDropIndicators();
        if (dropTarget.TargetCard is not null)
        {
            _dropIndicatorCard = dropTarget.TargetCard;
            _dropIndicatorCard.SetDropIndicator(dropTarget.AfterCard);
        }
        else
        {
            _dropIndicatorCell = dropTarget.Cell;
            _dropIndicatorCell.SetDropAtEnd(true);
        }
    }

    private void ClearDropIndicators()
    {
        _dropIndicatorCard?.ClearDropIndicator();
        _dropIndicatorCell?.SetDropAtEnd(false);
        _dropIndicatorCard = null;
        _dropIndicatorCell = null;
    }

    private static SwimlaneCellViewModel? FindSwimlaneCell(
        BoardViewModel boardViewModel,
        CardViewModel card) => boardViewModel.Swimlanes
        .SelectMany(lane => lane.Cells)
        .FirstOrDefault(cell => cell.Cards.Contains(card));

    private static int IndexOfCell(
        IReadOnlyList<SwimlaneCellViewModel> cells,
        SwimlaneCellViewModel targetCell)
    {
        for (var index = 0; index < cells.Count; index++)
        {
            if (ReferenceEquals(cells[index], targetCell))
            {
                return index;
            }
        }

        throw new InvalidOperationException(
            "La cella non appartiene alla fascia visualizzata.");
    }

    private Border? FindCardBorder(CardViewModel card) => this
        .GetVisualDescendants()
        .OfType<Border>()
        .FirstOrDefault(border =>
            border.Classes.Contains("card") &&
            ReferenceEquals(border.DataContext, card));

    private static bool TryGetMoveDirection(Key key, out CardMoveDirection direction)
    {
        switch (key)
        {
            case Key.Up:
                direction = CardMoveDirection.Up;
                return true;
            case Key.Down:
                direction = CardMoveDirection.Down;
                return true;
            case Key.Left:
                direction = CardMoveDirection.PreviousColumn;
                return true;
            case Key.Right:
                direction = CardMoveDirection.NextColumn;
                return true;
            default:
                direction = default;
                return false;
        }
    }

    private static bool TryGetLaneDelta(Key key, out int delta)
    {
        switch (key)
        {
            case Key.Up:
                delta = -1;
                return true;
            case Key.Down:
                delta = 1;
                return true;
            default:
                delta = 0;
                return false;
        }
    }

    private static SwimlaneViewModel? FindActiveLane(
        IReadOnlyList<SwimlaneViewModel> lanes,
        int sourceLaneIndex,
        int delta)
    {
        for (var index = sourceLaneIndex + delta;
             index >= 0 && index < lanes.Count;
             index += delta)
        {
            if (lanes[index].IsActive)
            {
                return lanes[index];
            }
        }

        return null;
    }

    private static int IndexOfLane(
        IReadOnlyList<SwimlaneViewModel> lanes,
        SwimlaneViewModel targetLane)
    {
        for (var index = 0; index < lanes.Count; index++)
        {
            if (ReferenceEquals(lanes[index], targetLane))
            {
                return index;
            }
        }

        throw new InvalidOperationException(
            "La fascia non appartiene alla board visualizzata.");
    }

    private static Border? FindAncestorBorderWithClass(Control control, string className)
    {
        var border = control.FindAncestorOfType<Border>(includeSelf: true);
        while (border is not null && !border.Classes.Contains(className))
        {
            border = border.FindAncestorOfType<Border>();
        }

        return border;
    }

    private readonly record struct DropTarget(
        SwimlaneCellViewModel Cell,
        int TargetCellIndex,
        CardViewModel? TargetCard,
        bool AfterCard);
}
