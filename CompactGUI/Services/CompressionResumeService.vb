Imports System.Text.Json

Imports CompactGUI.Core.Settings

Imports Microsoft.Extensions.Logging

Public NotInheritable Class CompressionResumeService

    Private ReadOnly _sessionsFile As IO.FileInfo
    Private ReadOnly _logger As ILogger(Of CompressionResumeService)
    Private ReadOnly _syncRoot As New Object
    Private ReadOnly _jsonOptions As New JsonSerializerOptions With {.WriteIndented = True}
    Private _sessions As Dictionary(Of String, SavedCompressionSession)

    Public Sub New(settingsService As ISettingsService, logger As ILogger(Of CompressionResumeService))
        _logger = logger
        _sessionsFile = New IO.FileInfo(IO.Path.Combine(settingsService.DataFolder.FullName, "compression-resume.json"))
        _sessions = LoadSessions()
    End Sub

    Public Function TryGetSession(folderPath As String, ByRef session As SavedCompressionSession) As Boolean
        Dim key = NormalizePath(folderPath)

        SyncLock _syncRoot
            Dim stored As SavedCompressionSession = Nothing
            If Not _sessions.TryGetValue(key, stored) Then Return False

            session = CloneSession(stored)
            Return True
        End SyncLock
    End Function

    Public Sub SaveSession(folder As CompressableFolder)
        Dim session = New SavedCompressionSession With {
            .FolderPath = NormalizePath(folder.FolderName),
            .SelectedCompressionMode = folder.CompressionOptions.SelectedCompressionMode,
            .SkipPoorlyCompressedFileTypes = folder.CompressionOptions.SkipPoorlyCompressedFileTypes,
            .SkipUserSubmittedFiletypes = folder.CompressionOptions.SkipUserSubmittedFiletypes,
            .WatchFolderForChanges = folder.CompressionOptions.WatchFolderForChanges,
            .SavedAt = DateTime.Now
        }

        SyncLock _syncRoot
            _sessions(session.FolderPath) = session
            WriteSessions()
        End SyncLock
    End Sub

    Public Sub RemoveSession(folderPath As String)
        Dim key = NormalizePath(folderPath)

        SyncLock _syncRoot
            If _sessions.Remove(key) Then WriteSessions()
        End SyncLock
    End Sub

    Private Function LoadSessions() As Dictionary(Of String, SavedCompressionSession)
        Dim loaded = New Dictionary(Of String, SavedCompressionSession)(StringComparer.OrdinalIgnoreCase)

        Try
            If Not _sessionsFile.Directory.Exists Then _sessionsFile.Directory.Create()
            If Not _sessionsFile.Exists Then Return loaded

            Dim json = IO.File.ReadAllText(_sessionsFile.FullName)
            If String.IsNullOrWhiteSpace(json) Then Return loaded

            Dim entries = JsonSerializer.Deserialize(Of List(Of SavedCompressionSession))(json, _jsonOptions)
            If entries Is Nothing Then Return loaded

            For Each entry In entries
                If String.IsNullOrWhiteSpace(entry.FolderPath) Then Continue For
                entry.FolderPath = NormalizePath(entry.FolderPath)
                loaded(entry.FolderPath) = entry
            Next
        Catch ex As Exception
            _logger.LogWarning(ex, "Could not load saved compression sessions from {SessionFile}", _sessionsFile.FullName)
        End Try

        Return loaded
    End Function

    Private Sub WriteSessions()
        Try
            If Not _sessionsFile.Directory.Exists Then _sessionsFile.Directory.Create()
            Dim snapshot = _sessions.Values.OrderBy(Function(entry) entry.FolderPath, StringComparer.OrdinalIgnoreCase).ToList()
            IO.File.WriteAllText(_sessionsFile.FullName, JsonSerializer.Serialize(snapshot, _jsonOptions))
        Catch ex As Exception
            _logger.LogWarning(ex, "Could not save compression sessions to {SessionFile}", _sessionsFile.FullName)
        End Try
    End Sub

    Private Shared Function CloneSession(source As SavedCompressionSession) As SavedCompressionSession
        Return New SavedCompressionSession With {
            .FolderPath = source.FolderPath,
            .SelectedCompressionMode = source.SelectedCompressionMode,
            .SkipPoorlyCompressedFileTypes = source.SkipPoorlyCompressedFileTypes,
            .SkipUserSubmittedFiletypes = source.SkipUserSubmittedFiletypes,
            .WatchFolderForChanges = source.WatchFolderForChanges,
            .SavedAt = source.SavedAt
        }
    End Function

    Private Shared Function NormalizePath(folderPath As String) As String
        If String.IsNullOrWhiteSpace(folderPath) Then Return String.Empty

        Try
            Return IO.Path.GetFullPath(folderPath).TrimEnd(IO.Path.DirectorySeparatorChar, IO.Path.AltDirectorySeparatorChar)
        Catch ex As Exception
            Return folderPath.TrimEnd(IO.Path.DirectorySeparatorChar, IO.Path.AltDirectorySeparatorChar)
        End Try
    End Function

End Class
