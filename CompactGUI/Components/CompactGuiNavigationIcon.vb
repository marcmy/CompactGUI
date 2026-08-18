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

        'Two opposing hollow arrows mirror CompactGUI's application icon without
        'introducing the four-arrow "move inward" glyph used by Fluent Icons.
        _northWestArrow = CreateArrowPath("M 2.8,2.8 L 8.2,4.0 L 6.35,5.85 L 9.75,9.25 L 9.25,9.75 L 5.85,6.35 L 4.0,8.2 Z")
        _southEastArrow = CreateArrowPath("M 17.2,17.2 L 11.8,16.0 L 13.65,14.15 L 10.25,10.75 L 10.75,10.25 L 14.15,13.65 L 16.0,11.8 Z")

        canvas.Children.Add(_northWestArrow)
        canvas.Children.Add(_southEastArrow)
        Return canvas
    End Function

    Private Function CreateArrowPath(data As String) As Path
        Return New Path With {
            .Data = Geometry.Parse(data),
            .Stroke = Foreground,
            .StrokeThickness = 1.15,
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
