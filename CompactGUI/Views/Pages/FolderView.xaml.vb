Public Class FolderView

    Private Sub CompressionDetailsGrid_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        Dim grid = TryCast(sender, DataGrid)
        If grid?.SelectedItem Is Nothing Then Return

        grid.ScrollIntoView(grid.SelectedItem)
    End Sub
End Class
