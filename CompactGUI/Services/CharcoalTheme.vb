Imports System.Windows.Controls
Imports System.Windows.Media
Imports System.Windows.Media.Imaging
Imports System.Windows.Shapes

Public Module CharcoalTheme

    Private ReadOnly MutedColorMap As New Dictionary(Of Color, Color) From {
        {Color.FromRgb(&H98, &HA9, &HB9), Color.FromRgb(&HB8, &HB8, &HB8)},
        {Color.FromRgb(&H4E, &H63, &H79), Color.FromRgb(&H50, &H50, &H50)},
        {Color.FromRgb(&H59, &H71, &H86), Color.FromRgb(&H68, &H68, &H68)},
        {Color.FromRgb(&H4B, &H61, &H75), Color.FromRgb(&H5A, &H5A, &H5A)},
        {Color.FromRgb(&H1F, &H34, &H48), Color.FromRgb(&H20, &H20, &H20)},
        {Color.FromRgb(&HD, &H2A, &H41), Color.FromRgb(&H18, &H18, &H18)}
    }

    Public Sub ApplyApplicationResources()
        If Application.Current Is Nothing Then Return

        'Secondary/status text stays neutral gray, while interactive selection accents use
        'the same crisp blue already present elsewhere in the CompactGUI navigation.
        Application.Current.Resources("AccentFillColorDisabledBrush") =
            New SolidColorBrush(Color.FromRgb(&HAA, &HAA, &HAA))

        Dim selectionBlue As New SolidColorBrush(Color.FromRgb(&H4C, &HA5, &HFF))
        Application.Current.Resources("CheckBoxCheckGlyphForeground") = selectionBlue
        Application.Current.Resources("CheckBoxCheckBorderBrush") = selectionBlue
    End Sub

    Public Sub ApplyRootBackground(root As Grid)
        If root Is Nothing Then Return

        Dim backgroundLayers = root.Children.OfType(Of Border)().Take(3).ToArray()
        If backgroundLayers.Length < 3 Then Return

        backgroundLayers(0).Background = New SolidColorBrush(Color.FromRgb(&H1A, &H1A, &H1A))

        'Keep the softly-lit charcoal look from the previous pass, but make the falloff much
        'broader and lower-contrast. ScRGB interpolation plus a one-level dither layer below
        'breaks up the visible WPF gradient contouring that showed up as large streaks.
        Dim topGlow As New RadialGradientBrush With {
            .Center = New Point(0.48, 0.02),
            .GradientOrigin = New Point(0.48, -0.12),
            .RadiusX = 1.35,
            .RadiusY = 1.7,
            .ColorInterpolationMode = ColorInterpolationMode.ScRgbLinearInterpolation
        }
        topGlow.GradientStops.Add(New GradientStop(Color.FromRgb(&H20, &H20, &H20), 0))
        topGlow.GradientStops.Add(New GradientStop(Color.FromRgb(&H1E, &H1E, &H1E), 0.46))
        topGlow.GradientStops.Add(New GradientStop(Color.FromRgb(&H1C, &H1C, &H1C), 0.78))
        topGlow.GradientStops.Add(New GradientStop(Color.FromRgb(&H1A, &H1A, &H1A), 1))
        backgroundLayers(1).Background = topGlow
        backgroundLayers(1).Opacity = 1

        backgroundLayers(2).Background = CreateDitherBrush()
        backgroundLayers(2).Opacity = 1
    End Sub

    Private Function CreateDitherBrush() As Brush
        Const tileSize As Integer = 64
        Const bytesPerPixel As Integer = 4
        Dim pixels(tileSize * tileSize * bytesPerPixel - 1) As Byte
        Dim random As New Random(&H434755)

        For pixelIndex = 0 To tileSize * tileSize - 1
            Dim offset = pixelIndex * bytesPerPixel
            Dim isLight = random.Next(0, 2) = 0

            pixels(offset) = If(isLight, CByte(255), CByte(0))
            pixels(offset + 1) = pixels(offset)
            pixels(offset + 2) = pixels(offset)
            pixels(offset + 3) = 1
        Next

        Dim bitmap As New WriteableBitmap(tileSize, tileSize, 96, 96, PixelFormats.Bgra32, Nothing)
        bitmap.WritePixels(New Int32Rect(0, 0, tileSize, tileSize), pixels, tileSize * bytesPerPixel, 0)
        bitmap.Freeze()

        Dim brush As New ImageBrush(bitmap) With {
            .TileMode = TileMode.Tile,
            .ViewportUnits = BrushMappingMode.Absolute,
            .ViewboxUnits = BrushMappingMode.Absolute,
            .Viewport = New Rect(0, 0, tileSize, tileSize),
            .Viewbox = New Rect(0, 0, tileSize, tileSize),
            .Stretch = Stretch.None
        }
        brush.Freeze()
        Return brush
    End Function

    Public Sub ApplyToVisualTree(root As DependencyObject)
        If root Is Nothing Then Return

        ApplyMutedPalette(root)

        For index = 0 To VisualTreeHelper.GetChildrenCount(root) - 1
            ApplyToVisualTree(VisualTreeHelper.GetChild(root, index))
        Next
    End Sub

    Private Sub ApplyMutedPalette(element As DependencyObject)
        Dim textBlock = TryCast(element, TextBlock)
        If textBlock IsNot Nothing Then textBlock.Foreground = MapBrush(textBlock.Foreground)

        Dim control = TryCast(element, Control)
        If control IsNot Nothing Then
            control.Foreground = MapBrush(control.Foreground)
            control.Background = MapBrush(control.Background)
            control.BorderBrush = MapBrush(control.BorderBrush)
        End If

        Dim border = TryCast(element, Border)
        If border IsNot Nothing Then
            border.Background = MapBrush(border.Background)
            border.BorderBrush = MapBrush(border.BorderBrush)
        End If

        Dim shape = TryCast(element, Shape)
        If shape IsNot Nothing Then
            shape.Fill = MapBrush(shape.Fill)
            shape.Stroke = MapBrush(shape.Stroke)
        End If
    End Sub

    Private Function MapBrush(brush As Brush) As Brush
        Dim solid = TryCast(brush, SolidColorBrush)
        If solid Is Nothing Then Return brush

        Dim replacement As Color
        If Not MutedColorMap.TryGetValue(solid.Color, replacement) Then Return brush

        Return New SolidColorBrush(Color.FromArgb(solid.Color.A, replacement.R, replacement.G, replacement.B))
    End Function

End Module
