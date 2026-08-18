Imports System.Windows.Controls
Imports System.Windows.Media
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

        'Use a dark neutral base and shape the light with broad radial falloff instead of
        'a full-height linear ramp. This avoids visible gradient bands while still keeping
        'the surface from looking flat.
        backgroundLayers(0).Background = New SolidColorBrush(Color.FromRgb(&H18, &H18, &H18))

        Dim topGlow As New RadialGradientBrush With {
            .Center = New Point(0.5, 0.08),
            .GradientOrigin = New Point(0.5, -0.02),
            .RadiusX = 0.95,
            .RadiusY = 1.08
        }
        topGlow.GradientStops.Add(New GradientStop(Color.FromArgb(&HFF, &H28, &H28, &H28), 0))
        topGlow.GradientStops.Add(New GradientStop(Color.FromArgb(&HE8, &H22, &H22, &H22), 0.34))
        topGlow.GradientStops.Add(New GradientStop(Color.FromArgb(&H78, &H1D, &H1D, &H1D), 0.7))
        topGlow.GradientStops.Add(New GradientStop(Color.FromArgb(&H0, &H18, &H18, &H18), 1))
        backgroundLayers(1).Background = topGlow
        backgroundLayers(1).Opacity = 1

        'A very light vignette gives the panel depth without turning the bottom half black.
        Dim vignette As New RadialGradientBrush With {
            .Center = New Point(0.5, 0.42),
            .GradientOrigin = New Point(0.5, 0.42),
            .RadiusX = 0.82,
            .RadiusY = 0.94
        }
        vignette.GradientStops.Add(New GradientStop(Color.FromArgb(&H0, 0, 0, 0), 0))
        vignette.GradientStops.Add(New GradientStop(Color.FromArgb(&H0, 0, 0, 0), 0.62))
        vignette.GradientStops.Add(New GradientStop(Color.FromArgb(&H2A, 0, 0, 0), 1))
        backgroundLayers(2).Background = vignette
        backgroundLayers(2).Opacity = 1
    End Sub

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
