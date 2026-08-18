Imports System.Windows.Media
Imports System.Windows.Shapes

Imports Wpf.Ui.Controls

Public NotInheritable Class CompactGuiNavigationIcon
    Inherits IconElement

    Private _northWestArrow As Path
    Private _southEastArrow As Path

    Protected Overrides Function InitializeChildren() As UIElement
        Dim canvas As New Canvas With {
            .Width = 20,
            .Height = 20,
            .SnapsToDevicePixels = True
        }

        'CompactGUI's mark is two opposing arrows compacting toward the centre.
        'Keep them as simple open outlines so the navigation glyph reads cleanly at 20 px.
        _northWestArrow = CreateArrowPath("M 2.7,2.7 L 9.0,9.0 M 9.0,9.0 L 9.0,5.6 M 9.0,9.0 L 5.6,9.0")
        _southEastArrow = CreateArrowPath("M 17.3,17.3 L 11.0,11.0 M 11.0,11.0 L 11.0,14.4 M 11.0,11.0 L 14.4,11.0")

        canvas.Children.Add(_northWestArrow)
        canvas.Children.Add(_southEastArrow)
        Return canvas
    End Function

    Private Function CreateArrowPath(data As String) As Path
        Return New Path With {
            .Data = Geometry.Parse(data),
            .Stroke = Foreground,
            .StrokeThickness = 1.55,
            .StrokeStartLineCap = PenLineCap.Round,
            .StrokeEndLineCap = PenLineCap.Round,
            .StrokeLineJoin = PenLineJoin.Round,
            .Fill = Brushes.Transparent
        }
    End Function

    Protected Overrides Sub OnForegroundChanged(args As DependencyPropertyChangedEventArgs)
        MyBase.OnForegroundChanged(args)

        Dim brush = TryCast(args.NewValue, Brush)
        If _northWestArrow IsNot Nothing Then _northWestArrow.Stroke = brush
        If _southEastArrow IsNot Nothing Then _southEastArrow.Stroke = brush
    End Sub
End Class
