Public Enum CompressionStopChoice
    SaveProgress
    UndoProgress
    LeaveAsIs
End Enum

Public Enum CompressionResumeChoice
    ResumeProgress
    DiscardSavedProgress
    Cancel
End Enum

Public NotInheritable Class CompressionRunResult
    Public Property Completed As Boolean
    Public Property StopChoice As CompressionStopChoice?
End Class

Public NotInheritable Class SavedCompressionSession
    Public Property FolderPath As String = String.Empty
    Public Property SelectedCompressionMode As Core.CompressionMode
    Public Property SkipPoorlyCompressedFileTypes As Boolean
    Public Property SkipUserSubmittedFiletypes As Boolean
    Public Property WatchFolderForChanges As Boolean
    Public Property SavedAt As DateTime = DateTime.Now
End Class
