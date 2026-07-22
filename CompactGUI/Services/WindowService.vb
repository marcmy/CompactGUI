Public Interface IWindowService
    Sub ShowMainWindow()
    Sub MinimizeMainWindow()
    Sub HideMainWindow()
    Function ShowMessageBox(title As String, content As String) As Task(Of Boolean)
    Function ShowCompressionStopDialog(folderName As String) As Task(Of CompressionStopChoice)
    Function ShowResumeCompressionDialog(session As SavedCompressionSession) As Task(Of CompressionResumeChoice)
End Interface


Public Class WindowService
    Implements IWindowService

    Public Sub ShowMainWindow() Implements IWindowService.ShowMainWindow
        Dim mainWindow = Application.GetService(Of MainWindow)()
        mainWindow.Show()
        mainWindow.WindowState = WindowState.Normal
        mainWindow.Topmost = True
        mainWindow.Activate()
        mainWindow.Topmost = False
    End Sub

    Public Sub MinimizeMainWindow() Implements IWindowService.MinimizeMainWindow
        Dim mainWindow = Application.GetService(Of MainWindow)()
        mainWindow.WindowState = WindowState.Minimized
    End Sub

    Public Sub HideMainWindow() Implements IWindowService.HideMainWindow
        Dim mainWindow = Application.GetService(Of MainWindow)()
        mainWindow.Hide()
    End Sub

    Public Async Function ShowMessageBox(title As String, content As String) As Task(Of Boolean) Implements IWindowService.ShowMessageBox
        Dim msgBox = New Wpf.Ui.Controls.MessageBox With {
               .Title = title,
               .Content = content,
               .IsPrimaryButtonEnabled = True,
               .PrimaryButtonText = LanguageHelper.GetString("UniYes"),
               .CloseButtonText = LanguageHelper.GetString("UniCancel")
           }
        Dim result = Await msgBox.ShowDialogAsync()
        Return result = Wpf.Ui.Controls.MessageBoxResult.Primary
    End Function

    Public Async Function ShowCompressionStopDialog(folderName As String) As Task(Of CompressionStopChoice) Implements IWindowService.ShowCompressionStopDialog
        Dim mainWindow = Application.GetService(Of MainWindow)()
        Dim cancelRequested = False
        Dim dialog As Wpf.Ui.Controls.ContentDialog = Nothing

        Dim titleGrid As New Grid With {.Width = 560}
        titleGrid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        titleGrid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})

        Dim titleText As New TextBlock With {
            .Text = LanguageHelper.GetString("CompressionStop_Title"),
            .VerticalAlignment = VerticalAlignment.Center
        }
        titleGrid.Children.Add(titleText)

        Dim cancelButton As New Wpf.Ui.Controls.Button With {
            .Content = "×",
            .Width = 32,
            .Height = 32,
            .Margin = New Thickness(12, -6, -6, -6),
            .ToolTip = LanguageHelper.GetString("UniCancel")
        }
        Grid.SetColumn(cancelButton, 1)
        titleGrid.Children.Add(cancelButton)

        dialog = New Wpf.Ui.Controls.ContentDialog(mainWindow.ContentDialogHost) With {
            .Title = titleGrid,
            .Content = String.Format(LanguageHelper.GetString("CompressionStop_Message"), folderName),
            .DialogWidth = 640,
            .PrimaryButtonText = LanguageHelper.GetString("CompressionStop_SaveProgress"),
            .SecondaryButtonText = LanguageHelper.GetString("CompressionStop_UndoProgress"),
            .CloseButtonText = LanguageHelper.GetString("CompressionStop_LeaveAsIs")
        }

        AddHandler cancelButton.Click,
            Sub()
                cancelRequested = True
                dialog.Hide(Wpf.Ui.Controls.ContentDialogResult.None)
            End Sub

        Select Case Await dialog.ShowAsync()
            Case Wpf.Ui.Controls.ContentDialogResult.Primary
                Return CompressionStopChoice.SaveProgress
            Case Wpf.Ui.Controls.ContentDialogResult.Secondary
                Return CompressionStopChoice.UndoProgress
            Case Else
                Return If(cancelRequested, CompressionStopChoice.Cancel, CompressionStopChoice.LeaveAsIs)
        End Select
    End Function

    Public Async Function ShowResumeCompressionDialog(session As SavedCompressionSession) As Task(Of CompressionResumeChoice) Implements IWindowService.ShowResumeCompressionDialog
        Dim msgBox = New Wpf.Ui.Controls.MessageBox With {
            .Title = LanguageHelper.GetString("CompressionResume_Title"),
            .Content = String.Format(LanguageHelper.GetString("CompressionResume_Message"), session.FolderPath, session.SelectedCompressionMode),
            .IsPrimaryButtonEnabled = True,
            .IsSecondaryButtonEnabled = True,
            .PrimaryButtonText = LanguageHelper.GetString("CompressionResume_Resume"),
            .SecondaryButtonText = LanguageHelper.GetString("CompressionResume_Discard"),
            .CloseButtonText = LanguageHelper.GetString("UniCancel")
        }

        Select Case Await msgBox.ShowDialogAsync()
            Case Wpf.Ui.Controls.MessageBoxResult.Primary
                Return CompressionResumeChoice.ResumeProgress
            Case Wpf.Ui.Controls.MessageBoxResult.Secondary
                Return CompressionResumeChoice.DiscardSavedProgress
            Case Else
                Return CompressionResumeChoice.Cancel
        End Select
    End Function
End Class
