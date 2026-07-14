using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WeeklyPlanner.App.Interaction;
using WeeklyPlanner.App.ViewModels;
using WeeklyPlanner.Core.Configuration;

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
    private ColumnViewModel? _dropIndicatorColumn;
    private bool _boardPanInProgress;
    private Point _boardPanStartPoint;
    private Vector _boardPanStartOffset;
    private AppSettingsService? _settingsService;
    private AppSettings? _applicationSettings;
    private bool _windowPlacementRestored;

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

        Opened += OnWindowOpened;
        PositionChanged += (_, _) => CaptureNormalWindowPlacement();
        Resized += (_, _) => CaptureNormalWindowPlacement();
    }

    public void ConfigureSettings(
        AppSettingsService settingsService,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(settings);

        _settingsService = settingsService;
        _applicationSettings = settings.Clone();
        _applicationSettings.Normalize();

        Width = _applicationSettings.WindowWidth;
        Height = _applicationSettings.WindowHeight;
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        if (_applicationSettings is null)
        {
            return;
        }

        var requestedPosition = _applicationSettings.WindowX is int x &&
                                _applicationSettings.WindowY is int y
            ? new PixelPoint(x, y)
            : (PixelPoint?)null;
        var storedScreen = requestedPosition is PixelPoint point
            ? Screens.ScreenFromPoint(point)
            : null;
        var targetScreen = storedScreen ?? Screens.Primary;

        if (targetScreen is not null)
        {
            var fittedSize = WindowPlacementCalculator.FitSizeToWorkingArea(
                _applicationSettings.WindowWidth,
                _applicationSettings.WindowHeight,
                targetScreen.WorkingArea,
                targetScreen.Scaling,
                MinWidth,
                MinHeight);
            Width = fittedSize.Width;
            Height = fittedSize.Height;
        }

        if (storedScreen is not null && requestedPosition is PixelPoint storedPosition)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = WindowPlacementCalculator.ClampPosition(
                storedPosition,
                new Size(Width, Height),
                storedScreen.WorkingArea,
                storedScreen.Scaling);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (_applicationSettings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        _windowPlacementRestored = true;
        CaptureNormalWindowPlacement();
    }

    private void CaptureNormalWindowPlacement()
    {
        if (!_windowPlacementRestored ||
            _applicationSettings is null ||
            WindowState != WindowState.Normal)
        {
            return;
        }

        _applicationSettings.WindowWidth = Math.Max(MinWidth, ClientSize.Width);
        _applicationSettings.WindowHeight = Math.Max(MinHeight, ClientSize.Height);
        _applicationSettings.WindowX = Position.X;
        _applicationSettings.WindowY = Position.Y;
    }

    private void PersistWindowPlacement()
    {
        if (_settingsService is null || _applicationSettings is null)
        {
            return;
        }

        CaptureNormalWindowPlacement();
        _applicationSettings.WindowMaximized = WindowState == WindowState.Maximized;

        try
        {
            _settingsService.Save(_applicationSettings);
        }
        catch
        {
            // Il salvataggio della geometria non deve impedire la chiusura dell'applicazione.
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (_shutdownCompleted || e.Cancel || DataContext is not IAsyncDisposable disposable)
        {
            return;
        }

        PersistWindowPlacement();
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
            Close();
        }
    }

    private async void OnOpenSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (_settingsService is null ||
            _applicationSettings is null ||
            DataContext is not BoardViewModel boardViewModel)
        {
            return;
        }

        var viewModel = new SettingsViewModel(
            _settingsService,
            _applicationSettings,
            boardViewModel.CanChangeIdentityAndDatabaseSettings);
        var window = new SettingsWindow
        {
            DataContext = viewModel,
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

    private void OnCardFieldLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control ||
            control.DataContext is not CardViewModel card ||
            DataContext is not BoardViewModel boardViewModel)
        {
            return;
        }

        var cardBorder = FindAncestorBorderWithClass(control, "card");
        if (cardBorder is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            async () =>
            {
                if (card.IsEditing && !cardBorder.IsKeyboardFocusWithin)
                {
                    await boardViewModel.CommitEditCommand.ExecuteAsync(card);
                }
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
        if (e.KeyModifiers != KeyModifiers.Alt ||
            sender is not Border cardBorder ||
            !ReferenceEquals(e.Source, cardBorder) ||
            cardBorder.DataContext is not CardViewModel card ||
            DataContext is not BoardViewModel { CanModifyBoard: true } boardViewModel ||
            !card.CanDrag ||
            !TryGetMoveDirection(e.Key, out var direction))
        {
            return;
        }

        var sourceColumn = boardViewModel.Columns
            .FirstOrDefault(column => column.Cards.Contains(card));
        if (sourceColumn is null)
        {
            return;
        }

        var sourceColumnIndex = boardViewModel.Columns.IndexOf(sourceColumn);
        var sourceCardIndex = sourceColumn.Cards.IndexOf(card);
        var cardCounts = boardViewModel.Columns.Select(column => column.Cards.Count).ToArray();
        if (!CardMovePlanner.TryCreate(
                sourceColumnIndex,
                sourceCardIndex,
                cardCounts,
                direction,
                out var plan))
        {
            return;
        }

        e.Handled = true;
        await boardViewModel.MoveCardAsync(
            card,
            boardViewModel.Columns[plan.TargetColumnIndex],
            plan.TargetIndex);

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
            !TryGetDropTarget(e, targetControl, out var dropTarget) ||
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
                !TryGetDropTarget(e, targetControl, out var dropTarget) ||
                !WouldChangePosition(boardViewModel, card, dropTarget))
            {
                e.DragEffects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
            await boardViewModel.MoveCardAsync(card, dropTarget.Column, dropTarget.TargetIndex);
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
        out DropTarget dropTarget)
    {
        var columnBorder = FindAncestorBorderWithClass(targetControl, "column");
        if (columnBorder?.DataContext is not ColumnViewModel targetColumn)
        {
            dropTarget = default;
            return false;
        }

        var cardBorder = FindAncestorBorderWithClass(targetControl, "card");
        if (cardBorder?.DataContext is not CardViewModel targetCard ||
            !targetColumn.Cards.Contains(targetCard))
        {
            dropTarget = new DropTarget(
                targetColumn,
                targetColumn.Cards.Count,
                TargetCard: null,
                AfterCard: false);
            return true;
        }

        var targetCardIndex = targetColumn.Cards.IndexOf(targetCard);
        var afterCard = e.GetPosition(cardBorder).Y >= cardBorder.Bounds.Height / 2;
        dropTarget = new DropTarget(
            targetColumn,
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
        var sourceColumn = boardViewModel.Columns.FirstOrDefault(column => column.Cards.Contains(card));
        if (sourceColumn is null)
        {
            return false;
        }

        var sourceColumnIndex = boardViewModel.Columns.IndexOf(sourceColumn);
        var targetColumnIndex = boardViewModel.Columns.IndexOf(dropTarget.Column);
        return CardMovePlanner.WouldChangePosition(
            sourceColumnIndex,
            sourceColumn.Cards.IndexOf(card),
            sourceColumn.Cards.Count,
            targetColumnIndex,
            dropTarget.TargetIndex);
    }

    private void SetDropIndicator(DropTarget dropTarget)
    {
        if (ReferenceEquals(_dropIndicatorCard, dropTarget.TargetCard) &&
            ReferenceEquals(_dropIndicatorColumn, dropTarget.TargetCard is null ? dropTarget.Column : null))
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
            _dropIndicatorColumn = dropTarget.Column;
            _dropIndicatorColumn.SetDropAtEnd(true);
        }
    }

    private void ClearDropIndicators()
    {
        _dropIndicatorCard?.ClearDropIndicator();
        _dropIndicatorColumn?.SetDropAtEnd(false);
        _dropIndicatorCard = null;
        _dropIndicatorColumn = null;
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
        ColumnViewModel Column,
        int TargetIndex,
        CardViewModel? TargetCard,
        bool AfterCard);
}
