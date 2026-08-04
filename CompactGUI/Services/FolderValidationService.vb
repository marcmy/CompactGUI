Imports CompactGUI.Core.SharedMethods

Public NotInheritable Class FolderValidationService

    Private ReadOnly _windowService As IWindowService

    Public Sub New(windowService As IWindowService)
        _windowService = windowService
    End Sub

    Public Async Function VerifyFolderAsync(folderPath As String) As Task(Of FolderVerificationResult)
        Dim result = VerifyFolder(folderPath)
        If result <> FolderVerificationResult.LZNT1Compressed Then Return result

        Dim shouldClearFlag = Await _windowService.ShowFolderCompressionFlagDialog(folderPath)
        If Not shouldClearFlag Then Return result

        Dim clearResult = Await Core.SharedMethods.ClearFolderLZNT1CompressionFlagAsync(folderPath)
        If Not clearResult.Succeeded Then
            Await _windowService.ShowFolderCompressionFlagClearErrorDialog(folderPath, clearResult.ErrorMessage)
        End If

        Return VerifyFolder(folderPath)
    End Function

End Class
