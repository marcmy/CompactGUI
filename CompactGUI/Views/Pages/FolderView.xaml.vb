Imports System.Windows.Controls.Primitives
Imports System.Windows.Media
Imports System.Windows.Shapes
Imports System.Windows.Threading

Public Class FolderView

    Private Shared ReadOnly AutoFollowDebounce As TimeSpan = TimeSpan.FromSeconds(5)
    Private Shared ReadOnly ColumnSeparatorBrush As Brush = New SolidColorBrush(Color.FromArgb(&H48, &HFF, &HFF, &HFF))
    Private Const MinimumScrollThumbHeight As Double = 28

    Private _compressionDetailsGrid As DataGrid
    Private _scrollThumb As Thumb
    Private _stopButton As Wpf.Ui.Controls.Button
    Private _isAutoFollowing As Boolean
    Private _isScrollThumbDragging As Boolean
    Private _suppressAutoFollowUntil As DateTime = DateTime.MinValue

    Public Sub New()
        InitializeComponent()
        AddHandler LayoutUpdated, AddressOf FolderView_LayoutUpdated
    End Sub

    Private Sub FolderView_LayoutUpdated(sender As Object, e As EventArgs)
        ConfigureCompressionDetailsGrid()
        ConfigureStopButton()
    End Sub

    Private Sub ConfigureCompressionDetailsGrid()
        Dim grid = FindVisualDescendant(Of DataGrid)(Me)
        If grid Is Nothing Then Return

        If Not Object.ReferenceEquals(_compressionDetailsGrid, grid) Then
            If _compressionDetailsGrid IsNot Nothing Then
                _compressionDetailsGrid.RemoveHandler(
                    ScrollViewer.ScrollChangedEvent,
                    New ScrollChangedEventHandler(AddressOf CompressionDetailsGrid_ScrollChanged))
            End If

            _compressionDetailsGrid = grid
            _compressionDetailsGrid.AddHandler(
                ScrollViewer.ScrollChangedEvent,
                New ScrollChangedEventHandler(AddressOf CompressionDetailsGrid_ScrollChanged),
                True)
        End If

        'The details table is designed to fit its available width. Do not expose a horizontal
        'scrollbar just because the DataGrid template reserves one for overflow scenarios.
        ScrollViewer.SetHorizontalScrollBarVisibility(grid, ScrollBarVisibility.Disabled)
        grid.CanUserResizeColumns = True

        ConfigureColumnHeaderSeparators(grid)
        ConfigureScrollThumb(grid)
    End Sub

    Private Shared Sub ConfigureColumnHeaderSeparators(grid As DataGrid)
        ForEachVisualDescendant(Of DataGridColumnHeader)(
            grid,
            Sub(header)
                'WPF-UI makes the resize boundary obvious only on hover. Keep a subtle line
                'visible at all times while preserving the normal resize gripper hit target.
                header.BorderBrush = ColumnSeparatorBrush
                header.BorderThickness = New Thickness(0, 0, 1, 0)
            End Sub)
    End Sub

    Private Sub ConfigureScrollThumb(grid As DataGrid)
        Dim verticalScrollBar = FindVisualDescendant(Of ScrollBar)(
            grid,
            Function(scrollBar) scrollBar.Orientation = Orientation.Vertical)
        If verticalScrollBar Is Nothing Then Return

        Dim thumb = FindVisualDescendant(Of Thumb)(verticalScrollBar)
        If thumb Is Nothing Then Return

        thumb.MinHeight = MinimumScrollThumbHeight

        If Object.ReferenceEquals(_scrollThumb, thumb) Then Return

        If _scrollThumb IsNot Nothing Then
            RemoveHandler _scrollThumb.DragStarted, AddressOf CompressionScrollThumb_DragStarted
            RemoveHandler _scrollThumb.DragDelta, AddressOf CompressionScrollThumb_DragDelta
            RemoveHandler _scrollThumb.DragCompleted, AddressOf CompressionScrollThumb_DragCompleted
        End If

        _scrollThumb = thumb
        AddHandler _scrollThumb.DragStarted, AddressOf CompressionScrollThumb_DragStarted
        AddHandler _scrollThumb.DragDelta, AddressOf CompressionScrollThumb_DragDelta
        AddHandler _scrollThumb.DragCompleted, AddressOf CompressionScrollThumb_DragCompleted
    End Sub

    Private Sub CompressionScrollThumb_DragStarted(sender As Object, e As DragStartedEventArgs)
        _isScrollThumbDragging = True
        SuppressAutoFollow()
    End Sub

    Private Sub CompressionScrollThumb_DragDelta(sender As Object, e As DragDeltaEventArgs)
        SuppressAutoFollow()
    End Sub

    Private Sub CompressionScrollThumb_DragCompleted(sender As Object, e As DragCompletedEventArgs)
        _isScrollThumbDragging = False
        SuppressAutoFollow()
    End Sub

    Private Sub CompressionDetailsGrid_ScrollChanged(sender As Object, e As ScrollChangedEventArgs)
        If _isAutoFollowing Then Return
        If e.VerticalChange = 0 Then Return

        SuppressAutoFollow()
    End Sub

    Private Sub SuppressAutoFollow()
        _suppressAutoFollowUntil = DateTime.UtcNow.Add(AutoFollowDebounce)
    End Sub

    Private Sub CompressionDetailsGrid_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        Dim grid = TryCast(sender, DataGrid)
        If grid?.SelectedItem Is Nothing Then Return

        ConfigureCompressionDetailsGrid()

        If _isScrollThumbDragging OrElse DateTime.UtcNow < _suppressAutoFollowUntil Then Return

        _isAutoFollowing = True
        Try
            grid.ScrollIntoView(grid.SelectedItem)
            grid.UpdateLayout()
        Finally
            grid.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                New Action(Sub() _isAutoFollowing = False))
        End Try
    End Sub

    Private Sub ConfigureStopButton()
        Dim viewModel = TryCast(DataContext, FolderViewModel)
        If viewModel Is Nothing Then Return

        Dim stopButton = FindVisualDescendant(Of Wpf.Ui.Controls.Button)(
            Me,
            Function(button) Object.ReferenceEquals(button.Command, viewModel.CancelCommand))
        If stopButton Is Nothing OrElse Object.ReferenceEquals(_stopButton, stopButton) Then Return

        _stopButton = stopButton
        _stopButton.Content = CreateStopIcon()
    End Sub

    Private Shared Function CreateStopIcon() As FrameworkElement
        Dim redBrush = TryCast(Application.Current.TryFindResource("PaletteRedBrush"), Brush)
        If redBrush Is Nothing Then redBrush = Brushes.IndianRed

        Dim icon As New Grid With {
            .Width = 18,
            .Height = 18
        }

        Dim octagon As New Polygon With {
            .Points = New PointCollection From {
                New Point(6, 1), New Point(12, 1),
                New Point(17, 6), New Point(17, 12),
                New Point(12, 17), New Point(6, 17),
                New Point(1, 12), New Point(1, 6)
            },
            .Fill = Brushes.Transparent,
            .Stroke = redBrush,
            .StrokeThickness = 1.7,
            .StrokeLineJoin = PenLineJoin.Round
        }

        Dim stopSquare As New Rectangle With {
            .Width = 6,
            .Height = 6,
            .HorizontalAlignment = HorizontalAlignment.Center,
            .VerticalAlignment = VerticalAlignment.Center,
            .Fill = redBrush,
            .RadiusX = 1,
            .RadiusY = 1
        }

        icon.Children.Add(octagon)
        icon.Children.Add(stopSquare)
        Return icon
    End Function

    Private Shared Sub ForEachVisualDescendant(Of T As DependencyObject)(root As DependencyObject, action As Action(Of T))
        If root Is Nothing OrElse action Is Nothing Then Return

        For index = 0 To VisualTreeHelper.GetChildrenCount(root) - 1
            Dim child = VisualTreeHelper.GetChild(root, index)
            Dim candidate = TryCast(child, T)
            If candidate IsNot Nothing Then action(candidate)

            ForEachVisualDescendant(child, action)
        Next
    End Sub

    Private Shared Function FindVisualDescendant(Of T As DependencyObject)(root As DependencyObject, Optional predicate As Func(Of T, Boolean) = Nothing) As T
        If root Is Nothing Then Return Nothing

        For index = 0 To VisualTreeHelper.GetChildrenCount(root) - 1
            Dim child = VisualTreeHelper.GetChild(root, index)
            Dim candidate = TryCast(child, T)

            If candidate IsNot Nothing AndAlso (predicate Is Nothing OrElse predicate(candidate)) Then
                Return candidate
            End If

            Dim nested = FindVisualDescendant(child, predicate)
            If nested IsNot Nothing Then Return nested
        Next

        Return Nothing
    End Function
End Class
