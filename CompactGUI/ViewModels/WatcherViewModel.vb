Imports System.Threading

Imports CommunityToolkit.Mvvm.ComponentModel
Imports CommunityToolkit.Mvvm.Input
Imports CommunityToolkit.Mvvm.Messaging

Imports CompactGUI.Watcher

Imports Wpf.Ui.Controls

Public NotInheritable Class WatcherViewModel : Inherits ObservableObject

    Private ReadOnly _snackbarService As CustomSnackBarService
    Private ReadOnly _folderValidationService As FolderValidationService
    Public ReadOnly Property Watcher As Watcher.Watcher

    Public Sub New(watcher As Watcher.Watcher, snackbarService As CustomSnackBarService, folderValidationService As FolderValidationService)
        Me.Watcher = watcher
        _snackbarService = snackbarService
        _folderValidationService = folderValidationService
    End Sub



    <RelayCommand>
    Public Async Function RunWatcher(token As CancellationToken) As Task
        Await Watcher.RunWatcher(True, token)
    End Function

    <RelayCommand>
    Public Sub CancelBackgrounding()
        RunWatcherCommand.Cancel()
        Watcher.CancelCurrentRun()
        Application.Current.Dispatcher.Invoke(Sub() CancelBackgroundingCommand.NotifyCanExecuteChanged())
    End Sub


    <RelayCommand>
    Private Async Function RemoveWatcher(watchedFolder As Watcher.WatchedFolder) As Task
        If watchedFolder Is Nothing Then Return
        Await Application.Current.Dispatcher.InvokeAsync(Sub() Watcher.RemoveWatched(watchedFolder))
    End Function

    <RelayCommand>
    Private Async Function RefreshWatched() As Task
        Watcher.RefreshWatchedFolderAvailability()
        Await Task.Run(Function() Watcher.ParseWatchers(True))
    End Function

    <RelayCommand>
    Private Async Function ReAnalyseWatched(watchedfolder As Watcher.WatchedFolder) As Task
        Await Task.Run(Function() Watcher.ParseSingleWatcher(watchedfolder))
    End Function



    <RelayCommand>
    Private Sub AddWatchedFolderToQueue(folder As Watcher.WatchedFolder)
        If folder Is Nothing OrElse Not folder.RefreshAvailability() Then Return

        WeakReferenceMessenger.Default.Send(New WatcherAddedFolderToQueueMessage(folder.Folder))
    End Sub

    <RelayCommand>
    Private Async Function ManuallyAddFolderToWatcher() As Task

        Dim folderSelector As New Microsoft.Win32.OpenFolderDialog
        folderSelector.ShowDialog()
        If folderSelector.FolderName = "" Then Return
        Dim path As String = folderSelector.FolderName

        Dim newFolder = Await AddFolderAsync(path)
        If newFolder Is Nothing Then Return

        Dim newWatched = New Watcher.WatchedFolder(newFolder.FolderName, newFolder.DisplayName) With {
           .IsSteamGame = TypeOf (newFolder) Is SteamFolder,
           .LastCompressedSize = 0,
           .LastUncompressedSize = 0,
           .LastCompressedDate = DateTime.UnixEpoch,
           .LastCheckedDate = DateTime.UnixEpoch,
           .LastCheckedSize = 0,
           .LastSystemModifiedDate = DateTime.UnixEpoch,
           .CompressionLevel = Core.WOFCompressionAlgorithm.NO_COMPRESSION}

        Watcher.AddOrUpdateWatched(newWatched)
        Await Watcher.Analyse(path, True)

    End Function



    Public Async Function AddFolderAsync(folderPath As String) As Task(Of CompressableFolder)

        Dim validation = Await _folderValidationService.VerifyFolderAsync(folderPath)
        If validation <> Core.SharedMethods.FolderVerificationResult.Valid Then
            _snackbarService.ShowInvalidFoldersMessage(
                New List(Of String) From {folderPath},
                New List(Of Core.SharedMethods.FolderVerificationResult) From {validation})
            Return Nothing
        End If

        Return Await CompressableFolderFactory.CreateCompressableFolder(folderPath)
    End Function



End Class
