Imports System.Collections.Concurrent
Imports System.Collections.ObjectModel
Imports System.Threading

Imports CompactGUI.Logging.Watcher

Imports Microsoft.Extensions.Logging

Imports Microsoft.Extensions.Logging.Abstractions

Public Class BackgroundCompactor

    Private Shared ReadOnly WatchdogPollInterval As TimeSpan = TimeSpan.FromSeconds(5)
    Private Shared ReadOnly NoProgressTimeout As TimeSpan = TimeSpan.FromMinutes(30)
    Private Shared ReadOnly CancellationGracePeriod As TimeSpan = TimeSpan.FromSeconds(5)

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
    Private ReadOnly _compactorLock As New Object
    Private ReadOnly _detachedCompactions As New ConcurrentDictionary(Of String, Task(Of Boolean))(StringComparer.OrdinalIgnoreCase)

    Private _excludedFileTypes As String()


    Private ReadOnly _logger As ILogger(Of Watcher)


    Public Event IsCompactingEvent As EventHandler(Of Boolean)

    Public Sub New(excludedFileTypes As String(), logger As ILogger(Of Watcher))

        _excludedFileTypes = excludedFileTypes
        _logger = logger
    End Sub


    Private Function CreateCompactor(folder As String,
                                     compressionLevel As Core.WOFCompressionAlgorithm,
                                     Optional excludedFileTypes As String() = Nothing) As Core.Compactor

        If compressionLevel = Core.WOFCompressionAlgorithm.NO_COMPRESSION Then Return Nothing

        Dim effectiveExclusions = If(excludedFileTypes Is Nothing, _excludedFileTypes, excludedFileTypes)
        Return New Core.Compactor(folder, compressionLevel, effectiveExclusions, New Core.Analyser(folder, NullLogger(Of Core.Analyser).Instance))

    End Function


    Public Async Function StartCompactingAsync(folders As IEnumerable(Of WatchedFolder)) As Task(Of Boolean)
        If IsCompactorActive Then Return False

        cancellationTokenSource?.Dispose()
        Dim runCancellation = New CancellationTokenSource()
        cancellationTokenSource = runCancellation

        WatcherLog.BackgroundCompactingStarted(_logger)
        IsCompactorActive = True
        isCompacting = True
        isCompactingPaused = False

        Dim currentProcess As Process = Process.GetCurrentProcess()

        Try
            currentProcess.PriorityClass = ProcessPriorityClass.Idle

            For Each folder In folders.ToList
                If runCancellation.IsCancellationRequested Then Return False

                If _detachedCompactions.ContainsKey(folder.Folder) Then
                    _logger.LogWarning("Skipping background compression for {Folder} because an earlier native compression task is still running.", folder.DisplayName)
                    Continue For
                End If

                folder.IsWorking = True
                Dim compactor As Core.Compactor = Nothing
                Dim disposeCompactor As Boolean = True

                Try
                    WatcherLog.CompactingFolder(_logger, folder.DisplayName)
                    Dim folderSkipList As String() = If(folder.SkipList Is Nothing, Nothing, folder.SkipList.ToArray())
                    compactor = CreateCompactor(folder.Folder, folder.CompressionLevel, folderSkipList)
                    If compactor Is Nothing Then Return False

                    'Pause can arrive after the background run starts but before this folder's
                    'native compactor exists. Publish the compactor and inherit the current pause
                    'state atomically so a newly-created compactor cannot run while the user is active.
                    SyncLock _compactorLock
                        _compactor = compactor
                        If isCompactingPaused Then compactor.Pause()
                    End SyncLock

                    Dim compactingTask = compactor.RunAsync(Nothing)

                    'Cancellation can arrive between selecting the folder and creating its compactor.
                    If runCancellation.IsCancellationRequested Then
                        compactor.Cancel()
                    End If

                    Dim waitResult = Await WaitForCompactorAsync(compactor, compactingTask, folder, runCancellation.Token)
                    If Not waitResult.TaskCompleted Then
                        'The native operation did not return after cancellation. Its task now owns
                        'the compactor lifetime and will dispose it when Windows finally returns.
                        disposeCompactor = False
                        Return False
                    End If

                    Dim result = waitResult.Result
                    If runCancellation.IsCancellationRequested OrElse Not result Then
                        Trace.WriteLine("Compacting cancelled by user.")
                        Return False
                    End If

                    If folders.Contains(folder) Then
                        'Ensure the folder is still in the original collection before updating.
                        Using analyser As New Core.Analyser(folder.Folder, NullLogger(Of Core.Analyser).Instance)
                            Await analyser.GetAnalysedFilesAsync(runCancellation.Token)

                            If runCancellation.IsCancellationRequested Then Return False

                            folder.LastCheckedDate = DateTime.Now
                            folder.LastCheckedSize = analyser.CompressedBytes
                            folder.LastCompressedSize = analyser.CompressedBytes
                            folder.LastSystemModifiedDate = DateTime.Now

                            folder.LastCompressedDate = DateTime.Now
                            folder.HasTargetChanged = False
                        End Using
                    End If

                    folder.RefreshProperties()
                    WatcherLog.FinishedCompactingFolder(_logger, folder.DisplayName)
                Finally
                    folder.IsWorking = False

                    SyncLock _compactorLock
                        If Object.ReferenceEquals(_compactor, compactor) Then
                            _compactor = Nothing
                        End If
                    End SyncLock

                    If disposeCompactor Then
                        compactor?.Dispose()
                    End If
                End Try
            Next

            WatcherLog.BackgroundCompactingFinished(_logger)
            Return True
        Catch ex As OperationCanceledException
            Trace.WriteLine("Compacting cancelled by user.")
            Return False
        Finally
            'Each folder task owns its compactor lifetime. A task detached after a stuck
            'native call disposes its compactor only after that task actually exits.
            isCompacting = False
            isCompactingPaused = False
            IsCompactorActive = False

            SyncLock _compactorLock
                _compactor = Nothing
            End SyncLock

            If Object.ReferenceEquals(cancellationTokenSource, runCancellation) Then
                cancellationTokenSource = Nothing
            End If
            runCancellation.Dispose()

            Try
                currentProcess.PriorityClass = ProcessPriorityClass.Normal
            Catch ex As Exception
                _logger.LogDebug(ex, "Unable to restore CompactGUI process priority.")
            End Try
        End Try
    End Function

    Private Async Function WaitForCompactorAsync(compactor As Core.Compactor,
                                                   compactingTask As Task(Of Boolean),
                                                   folder As WatchedFolder,
                                                   cancellationToken As CancellationToken) As Task(Of (TaskCompleted As Boolean, Result As Boolean))
        Dim lastProgressVersion = compactor.ProgressVersion
        Dim stalledFor = TimeSpan.Zero

        Do
            If compactingTask.IsCompleted Then
                Return (TaskCompleted:=True, Result:=Await compactingTask)
            End If

            If cancellationToken.IsCancellationRequested Then
                Return Await StopOrDetachCompactorAsync(compactor, compactingTask, folder, "user cancellation")
            End If

            Await Task.Delay(WatchdogPollInterval)

            If compactingTask.IsCompleted Then
                Return (TaskCompleted:=True, Result:=Await compactingTask)
            End If

            If cancellationToken.IsCancellationRequested Then
                Return Await StopOrDetachCompactorAsync(compactor, compactingTask, folder, "user cancellation")
            End If

            'A background run can legitimately remain paused while the user is active.
            'Paused time must never count toward the no-progress watchdog.
            If isCompactingPaused Then
                stalledFor = TimeSpan.Zero
                lastProgressVersion = compactor.ProgressVersion
                Continue Do
            End If

            Dim currentProgressVersion = compactor.ProgressVersion
            If currentProgressVersion <> lastProgressVersion Then
                lastProgressVersion = currentProgressVersion
                stalledFor = TimeSpan.Zero
                Continue Do
            End If

            stalledFor = stalledFor.Add(WatchdogPollInterval)
            If stalledFor >= NoProgressTimeout Then
                _logger.LogWarning(
                    "Background compression made no progress for {TimeoutMinutes} minutes in {Folder}. Phase: {Phase}. File: {File}. Cancelling the run.",
                    NoProgressTimeout.TotalMinutes,
                    folder.DisplayName,
                    compactor.CurrentPhase,
                    If(compactor.CurrentFile, "<none>"))

                Return Await StopOrDetachCompactorAsync(compactor, compactingTask, folder, "watchdog timeout")
            End If
        Loop
    End Function

    Private Async Function StopOrDetachCompactorAsync(compactor As Core.Compactor,
                                                        compactingTask As Task(Of Boolean),
                                                        folder As WatchedFolder,
                                                        reason As String) As Task(Of (TaskCompleted As Boolean, Result As Boolean))
        Try
            compactor.Cancel()
        Catch ex As ObjectDisposedException
            'The task completed while cancellation was being requested.
        End Try

        Dim completed = Await Task.WhenAny(compactingTask, Task.Delay(CancellationGracePeriod))
        If completed Is compactingTask Then
            Return (TaskCompleted:=True, Result:=Await compactingTask)
        End If

        _logger.LogWarning(
            "Background compression for {Folder} did not stop within {GraceSeconds} seconds after {Reason}. Releasing the watcher and skipping this folder until the native task exits. Phase: {Phase}. File: {File}.",
            folder.DisplayName,
            CancellationGracePeriod.TotalSeconds,
            reason,
            compactor.CurrentPhase,
            If(compactor.CurrentFile, "<none>"))

        RegisterDetachedCompaction(folder.Folder, folder.DisplayName, compactor, compactingTask)
        Return (TaskCompleted:=False, Result:=False)
    End Function

    Private Sub RegisterDetachedCompaction(folderPath As String,
                                           displayName As String,
                                           compactor As Core.Compactor,
                                           compactingTask As Task(Of Boolean))
        If Not _detachedCompactions.TryAdd(folderPath, compactingTask) Then Return

        compactingTask.ContinueWith(
            Sub(task)
                Try
                    If task.IsFaulted Then
                        _logger.LogError(task.Exception, "Detached background compression for {Folder} exited with an error.", displayName)
                    Else
                        _logger.LogInformation("Detached background compression for {Folder} has exited and can be scheduled again.", displayName)
                    End If
                Finally
                    compactor.Dispose()
                    Dim removedTask As Task(Of Boolean) = Nothing
                    _detachedCompactions.TryRemove(folderPath, removedTask)
                End Try
            End Sub,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default)
    End Sub

    Public Sub PauseCompacting()
        If Not isCompacting OrElse isCompactingPaused Then
            Return
        End If

        WatcherLog.PausingBackgroundCompactor(_logger)
        isCompactingPaused = True ' Indicate compacting is paused

        Dim compactor As Core.Compactor
        SyncLock _compactorLock
            compactor = _compactor
        End SyncLock
        compactor?.Pause()
    End Sub

    Public Sub ResumeCompacting()
        If Not isCompactingPaused OrElse Not isCompacting Then
            Return
        End If

        WatcherLog.ResumingBackgroundCompactor(_logger)
        isCompactingPaused = False ' Indicate compacting is no longer paused

        Dim compactor As Core.Compactor
        SyncLock _compactorLock
            compactor = _compactor
        End SyncLock
        compactor?.Resume()
    End Sub

    Public Sub CancelCompacting()
        If Not isCompacting Then
            Return
        End If

        Debug.WriteLine("Cancelling background compactor...")

        Try
            cancellationTokenSource?.Cancel()
        Catch ex As ObjectDisposedException
            'The run completed while cancellation was being requested.
        End Try

        Dim compactor As Core.Compactor
        SyncLock _compactorLock
            compactor = _compactor
        End SyncLock
        compactor?.Cancel()
        isCompactingPaused = False ' Reset pause state on cancellation
    End Sub

End Class
