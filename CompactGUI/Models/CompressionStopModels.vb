' Decisions shown when stopping an active manual compression run.
Public Enum CompressionStopChoice
    SaveProgress
    UndoProgress
    LeaveAsIs
    Cancel
End Enum

' Decisions shown when a folder has a saved resumable compression session.
Public Enum CompressionResumeChoice
    ResumeProgress
    DiscardSavedProgress
    Cancel
End Enum

Public NotInheritable Class CompressionRunResult
    Public Property Completed As Boolean
    Public Property HadWork As Boolean
    Public Property StopChoice As CompressionStopChoice?
End Class

Public NotInheritable Class SavedCompressionSession
    Public Const CurrentResumeDataVersion As Integer = 1

    Public Property FolderPath As String = String.Empty
    Public Property SelectedCompressionMode As Core.CompressionMode
    Public Property SkipPoorlyCompressedFileTypes As Boolean
    Public Property SkipUserSubmittedFiletypes As Boolean
    Public Property SkipList As List(Of String)
    Public Property SkipListEnabled As Boolean = True
    Public Property WatchFolderForChanges As Boolean
    Public Property ResumeDataVersion As Integer
    Public Property TotalFiles As Integer
    Public Property TotalBytes As Long
    Public Property ProcessedFiles As Integer
    Public Property ProcessedBytes As Long
    Public Property FailedFiles As Integer
    Public Property FailedFilePaths As New List(Of String)
    Public Property SavedAt As DateTime = DateTime.Now

    <System.Text.Json.Serialization.JsonIgnore>
    Public ReadOnly Property HasProgressCheckpoint As Boolean
        Get
            Return ResumeDataVersion >= CurrentResumeDataVersion
        End Get
    End Property
End Class
