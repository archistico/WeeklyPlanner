using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WeeklyPlanner.App.ViewModels;

namespace WeeklyPlanner.App.Views;

public partial class MainWindow : Window
{
    private static readonly DataFormat<string> CardDragFormat =
        DataFormat.CreateStringApplicationFormat("weeklyplanner-card");
    private const double DragThreshold = 5;

    private CardViewModel? _pendingDragCard;
    private Point _dragStartPoint;
    private bool _dragInProgress;

    public MainWindow()
    {
        InitializeComponent();

        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDropHandler(this, OnDrop);
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

        var cardBorder = handle.FindAncestorOfType<Border>();
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
        }
    }

    private void OnCardDragHandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pendingDragCard = null;
        e.Pointer.Capture(null);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(CardDragFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var cardIdText = e.DataTransfer.TryGetValue(CardDragFormat);
        if (!long.TryParse(
                cardIdText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var cardId) ||
            e.Source is not Control targetControl ||
            DataContext is not BoardViewModel boardViewModel)
        {
            return;
        }

        var card = boardViewModel.Columns
            .SelectMany(column => column.Cards)
            .FirstOrDefault(candidate => candidate.Model.Id == cardId);
        if (card is null || !card.CanDrag)
        {
            return;
        }

        var columnBorder = targetControl.FindAncestorOfType<Border>(includeSelf: true);
        while (columnBorder is not null && !columnBorder.Classes.Contains("column"))
        {
            columnBorder = columnBorder.FindAncestorOfType<Border>();
        }

        if (columnBorder?.DataContext is not ColumnViewModel targetColumn)
        {
            return;
        }

        var targetIndex = GetDropIndex(e, targetControl, targetColumn);

        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
        await boardViewModel.MoveCardAsync(card, targetColumn, targetIndex);
    }

    private static int GetDropIndex(
        DragEventArgs e,
        Control targetControl,
        ColumnViewModel targetColumn)
    {
        var cardBorder = FindAncestorBorderWithClass(targetControl, "card");
        if (cardBorder?.DataContext is not CardViewModel targetCard)
        {
            return targetColumn.Cards.Count;
        }

        var targetCardIndex = targetColumn.Cards.IndexOf(targetCard);
        if (targetCardIndex < 0)
        {
            return targetColumn.Cards.Count;
        }

        var pointerPosition = e.GetPosition(cardBorder);
        return pointerPosition.Y >= cardBorder.Bounds.Height / 2
            ? targetCardIndex + 1
            : targetCardIndex;
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
}
