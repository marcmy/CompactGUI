
Imports CommunityToolkit.Mvvm.ComponentModel
Imports CommunityToolkit.Mvvm.Input
Imports CommunityToolkit.Mvvm.Messaging
Imports CommunityToolkit.Mvvm.Messaging.Messages

Imports CompactGUI.Core.Settings


Partial Public Class MainWindowViewModel : Inherits ObservableRecipient : Implements IRecipient(Of PropertyChangedMessage(Of CompressableFolder))

    <ObservableProperty>
    Private _BackgroundImage As BitmapImage

    Private ReadOnly _watcher As Watcher.Watcher
    Private ReadOnly _windowService As IWindowService
    Private ReadOnly _settingsService As ISettingsService
    Private _allowClose As Boolean = False
    Private _isExitInProgress As Boolean = False

    Public Sub New(windowService As IWindowService, watcher As Watcher.Watcher, settingsService As ISettingsService)
        _watcher = watcher
        _windowService = windowService
        _settingsService = settingsService
    End Sub

    Public ReadOnly Property IsAdmin As Boolean
        Get
            Dim principal = New Security.Principal.WindowsPrincipal(Security.Principal.WindowsIdentity.GetCurrent())
            Return principal.IsInRole(Security.Principal.WindowsBuiltInRole.Administrator)
        End Get
    End Property


    <RelayCommand>
    Private Sub NotifyIconOpen()
        _windowService.ShowMainWindow()
    End Sub


    <RelayCommand>
    Private Async Function NotifyIconExit() As Task
        If _isExitInProgress Then Return
        _isExitInProgress = True

        Try
            If _watcher.WatchedFolders.Count <> 0 Then
                Dim message As String = String.Format(LanguageHelper.GetString("MessageBox_ExitText"), _watcher.WatchedFolders.Count)
                Dim confirmed = Await _windowService.ShowMessageBox(LanguageHelper.GetString("Title_CompactGUI"), message)
                If Not confirmed Then Return
            End If

            If Not Await PrepareManualCompressionForExitAsync() Then Return

            If _watcher.WatchedFolders.Count <> 0 Then _watcher.WriteToFile()
            _settingsService.SaveSettings()
            _allowClose = True
            Application.Current.Shutdown()
        Finally
            If Not _allowClose Then _isExitInProgress = False
        End Try
    End Function


    Private Async Function PrepareManualCompressionForExitAsync() As Task(Of Boolean)
        Dim homeViewModel = Application.GetService(Of HomeViewModel)()
        If homeViewModel Is Nothing OrElse Not homeViewModel.Compressing Then Return True

        Dim activeFolder = homeViewModel.GetActiveManualCompressionFolder()
        If activeFolder Is Nothing Then
            Await homeViewModel.StopManualCompressionForExitAsync(Nothing, Nothing)
            Return True
        End If

        _windowService.ShowMainWindow()

        Dim pausedForStopDialog = False
        If activeFolder.FolderActionState = ActionState.Working Then
            Try
                activeFolder.Compressor?.Pause()
                activeFolder.FolderActionState = ActionState.Paused
                pausedForStopDialog = True
            Catch ex As OperationCanceledException
                Await homeViewModel.StopManualCompressionForExitAsync(Nothing, Nothing)
                Return True
            Catch ex As ObjectDisposedException
                Await homeViewModel.StopManualCompressionForExitAsync(Nothing, Nothing)
                Return True
            End Try
        End If

        Dim choice = Await _windowService.ShowCompressionStopDialog(activeFolder.DisplayName)
        If choice = CompressionStopChoice.Cancel Then
            If pausedForStopDialog AndAlso activeFolder.FolderActionState = ActionState.Paused Then
                Try
                    activeFolder.Compressor?.Resume()
                    activeFolder.FolderActionState = ActionState.Working
                Catch ex As OperationCanceledException
                    'The compression finished while the stop dialog was open.
                Catch ex As ObjectDisposedException
                    'The compression finished while the stop dialog was open.
                End Try
            End If
            Return False
        End If

        Await homeViewModel.StopManualCompressionForExitAsync(activeFolder, choice)
        Return True
    End Function


    <RelayCommand>
    Private Async Function Closing(e As ComponentModel.CancelEventArgs) As Task
        If e Is Nothing Then Return
        If _allowClose Then
            e.Cancel = False
            Return
        End If

        Dim forceExit = Keyboard.Modifiers = ModifierKeys.Shift

        If Not forceExit AndAlso _watcher.WatchedFolders.Count <> 0 Then
            e.Cancel = True
            _windowService.MinimizeMainWindow()
            _watcher.WriteToFile()
            _windowService.HideMainWindow()
            Return
        End If

        'This close would actually exit the application. Cancel it synchronously so an
        'active manual compression run can use the normal stop/save/undo prompt first.
        e.Cancel = True
        If _isExitInProgress Then Return
        _isExitInProgress = True

        Try
            If Not Await PrepareManualCompressionForExitAsync() Then Return

            If _watcher.WatchedFolders.Count <> 0 Then _watcher.WriteToFile()
            _settingsService.SaveSettings()
            _allowClose = True
            Application.Current.Shutdown()
        Finally
            If Not _allowClose Then _isExitInProgress = False
        End Try
    End Function


    Public Sub Receive(message As PropertyChangedMessage(Of CompressableFolder)) Implements IRecipient(Of PropertyChangedMessage(Of CompressableFolder)).Receive

        If message.Sender.GetType() IsNot GetType(HomeViewModel) Then Return
        If message.PropertyName <> NameOf(HomeViewModel.SelectedFolder) Then Return
        BackgroundImage = message.NewValue?.FolderBGImage

    End Sub
End Class
