Imports System.ComponentModel
Imports System.Windows.Media
Imports System.Windows.Threading

Class HomePage

    Private _viewModel As HomeViewModel


    Sub New(viewmodel As HomeViewModel)

        ' This call is required by the designer.
        InitializeComponent()
        _viewModel = viewmodel
        DataContext = viewmodel

        ScrollViewer.SetCanContentScroll(Me, False)

        AddHandler Loaded, AddressOf HomePage_Loaded
        AddHandler _viewModel.PropertyChanged, AddressOf ViewModel_PropertyChanged

        ' Add any initialization after the InitializeComponent() call.

    End Sub

    Private Sub HomePage_Loaded(sender As Object, e As RoutedEventArgs)
        UpdateCompressButtonText()
    End Sub

    Private Sub ViewModel_PropertyChanged(sender As Object, e As PropertyChangedEventArgs)
        If e.PropertyName <> NameOf(HomeViewModel.HomeViewModelState) Then Return
        Dispatcher.BeginInvoke(AddressOf UpdateCompressButtonText, DispatcherPriority.Loaded)
    End Sub

    Private Sub UpdateCompressButtonText()
        Dim compressButton = FindVisualDescendant(Of Button)(
            Me,
            Function(button) Object.ReferenceEquals(button.Command, _viewModel.CompressAllCommand))

        If compressButton IsNot Nothing Then compressButton.Content = "Compress Now"
    End Sub

    Private Shared Function FindVisualDescendant(Of T As DependencyObject)(root As DependencyObject, predicate As Func(Of T, Boolean)) As T
        If root Is Nothing Then Return Nothing

        For index = 0 To VisualTreeHelper.GetChildrenCount(root) - 1
            Dim child = VisualTreeHelper.GetChild(root, index)
            Dim candidate = TryCast(child, T)
            If candidate IsNot Nothing AndAlso predicate(candidate) Then Return candidate

            Dim nested = FindVisualDescendant(child, predicate)
            If nested IsNot Nothing Then Return nested
        Next

        Return Nothing
    End Function

    Private Async Sub AddFolderButton_Click(sender As Object, e As RoutedEventArgs) Handles BtnAddFolder1.Click, BtnAddFolder2.Click
        Dim folderBrowser As New Microsoft.Win32.OpenFolderDialog With {
            .Title = "Select a folder to compress",
            .Multiselect = True,
            .ValidateNames = True
        }
        folderBrowser.ShowDialog()

        If folderBrowser.FolderNames.Length > 0 Then
            Await _viewModel.AddFoldersAsync(folderBrowser.FolderNames)
        End If
    End Sub

    Private Sub Root_DragOver(sender As Object, e As DragEventArgs)

        If e.Data.GetDataPresent(DataFormats.FileDrop) Then
            Dim paths As String() = e.Data.GetData(DataFormats.FileDrop)

            If paths.All(Function(path) IO.Directory.Exists(path)) Then
                e.Effects = DragDropEffects.Copy
            Else
                e.Effects = DragDropEffects.None
            End If


        Else
            e.Effects = DragDropEffects.None
        End If

        e.Handled = True

    End Sub

    Private Sub Root_Drop(sender As Object, e As DragEventArgs)

        If e.Data.GetDataPresent(DataFormats.FileDrop) Then
            Dim paths As String() = e.Data.GetData(DataFormats.FileDrop)
            If paths.All(Function(path) IO.Directory.Exists(path)) Then
                _viewModel.AddFoldersAsync(paths).ConfigureAwait(False)
            End If
        End If

    End Sub
End Class
