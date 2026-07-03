Imports System.Collections.ObjectModel
Imports System.Threading

Imports CompactGUI.Logging.Watcher

Imports Microsoft.Extensions.Logging

Imports Microsoft.Extensions.Logging.Abstractions

Public Class BackgroundCompactor

    Private _IsCompactorActive As Boolean = False
    Public Property IsCompactorActive As Boolean
        Get
            Return _IsCompactorActive
        End Get
        Set(value As Boolean)
            If _IsCompactorActive = value Then Return
            _IsCompactorActive = value
            RaiseEvent IsCompactingEvent(Me, value)
        End Set
    End Property

    Private cancellationTokenSource As CancellationTokenSource
    Private isCompacting As Boolean = False
    Private isCompactingPaused As Boolean = False ' Track if compacting is paused

    Private _compactor As Core.Compactor

    Private _excludedFileTypes As String()


    Private ReadOnly _logger As ILogger(Of Watcher)


    Public Event IsCompactingEvent As EventHandler(Of Boolean)

    Public Sub New(excludedFileTypes As String(), logger As ILogger(Of Watcher))

        _excludedFileTypes = excludedFileTypes
        _logger = logger
    End Sub


    Public Function BeginCompacting(folder As String, compressionLevel As Core.WOFCompressionAlgorithm) As Task(Of Boolean)

        If compressionLevel = Core.WOFCompressionAlgorithm.NO_COMPRESSION Then Return Task.FromResult(False)

        _compactor = New Core.Compactor(folder, compressionLevel, _excludedFileTypes, New Core.Analyser(folder, NullLogger(Of Core.Analyser).Instance))

        Return _compactor.RunAsync(Nothing)

    End Function


    Public Async Function StartCompactingAsync(folders As IEnumerable(Of WatchedFolder)) As Task(Of Boolean)
        If IsCompactorActive Then Return False

        cancellationTokenSource?.Dispose()
        cancellationTokenSource = New CancellationTokenSource()

        WatcherLog.BackgroundCompactingStarted(_logger)
        IsCompactorActive = True
        isCompacting = True
        isCompactingPaused = False

        Dim currentProcess As Process = Process.GetCurrentProcess()

        Try
            currentProcess.PriorityClass = ProcessPriorityClass.Idle

            For Each folder In folders.ToList
                If cancellationTokenSource.IsCancellationRequested Then Return False

                folder.IsWorking = True

                Try
                    WatcherLog.CompactingFolder(_logger, folder.DisplayName)
                    Dim compactingTask = BeginCompacting(folder.Folder, folder.CompressionLevel)

                    'Cancellation can arrive between selecting the folder and creating its compactor.
                    If cancellationTokenSource.IsCancellationRequested Then
                        _compactor?.Cancel()
                    End If

                    Dim result = Await compactingTask

                    If cancellationTokenSource.IsCancellationRequested OrElse Not result Then
                        Trace.WriteLine("Compacting cancelled by user.")
                        Return False
                    End If

                    If folders.Contains(folder) Then
                        'Ensure the folder is still in the original collection before updating.
                        Using analyser As New Core.Analyser(folder.Folder, NullLogger(Of Core.Analyser).Instance)
                            Dim analysed = Await analyser.GetAnalysedFilesAsync(cancellationTokenSource.Token)

                            If cancellationTokenSource.IsCancellationRequested Then Return False

                            folder.LastCheckedDate = DateTime.Now
                            folder.LastCheckedSize = analyser.CompressedBytes
                            folder.LastCompressedSize = analyser.CompressedBytes
                            folder.LastSystemModifiedDate = DateTime.Now

                            If analysed IsNot Nothing AndAlso analysed.Count > 0 Then
                                folder.CompressionLevel = analysed.Select(Function(f) f.CompressionMode).Max
                            End If

                            folder.LastCompressedDate = DateTime.Now
                            folder.HasTargetChanged = False
                        End Using
                    End If

                    folder.RefreshProperties()
                    WatcherLog.FinishedCompactingFolder(_logger, folder.DisplayName)
                Finally
                    folder.IsWorking = False
                    _compactor?.Dispose()
                    _compactor = Nothing
                End Try
            Next

            WatcherLog.BackgroundCompactingFinished(_logger)
            Return True
        Catch ex As OperationCanceledException
            Trace.WriteLine("Compacting cancelled by user.")
            Return False
        Finally
            'The worker that created the compactor owns its disposal. CancelCompacting
            'only signals cancellation so native WOF calls cannot race freed state.
            isCompacting = False
            isCompactingPaused = False
            IsCompactorActive = False

            _compactor?.Dispose()
            _compactor = Nothing

            cancellationTokenSource?.Dispose()
            cancellationTokenSource = Nothing

            Try
                currentProcess.PriorityClass = ProcessPriorityClass.Normal
            Catch ex As Exception
                _logger.LogDebug(ex, "Unable to restore CompactGUI process priority.")
            End Try
        End Try
    End Function

    Public Sub PauseCompacting()
        If Not isCompacting OrElse isCompactingPaused Then
            Return
        End If

        WatcherLog.PausingBackgroundCompactor(_logger)
        isCompactingPaused = True ' Indicate compacting is paused
        _compactor?.Pause()
    End Sub

    Public Sub ResumeCompacting()
        If Not isCompactingPaused OrElse Not isCompacting Then
            Return
        End If

        WatcherLog.ResumingBackgroundCompactor(_logger)
        isCompactingPaused = False ' Indicate compacting is no longer paused
        _compactor?.Resume()
    End Sub

    Public Sub CancelCompacting()
        If Not isCompacting Then
            Return
        End If

        Debug.WriteLine("Cancelling background compactor...")
        cancellationTokenSource?.Cancel()
        _compactor?.Cancel()
        isCompactingPaused = False ' Reset pause state on cancellation
    End Sub

End Class
