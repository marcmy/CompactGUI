Imports System.Collections.Concurrent
Imports System.Collections.ObjectModel
Imports System.Threading

Imports CompactGUI.Core
Imports CompactGUI.Core.Settings

'Imports Microsoft.CodeAnalysis.Diagnostics

Imports Microsoft.Extensions.Logging

Public Class CompressableFolderService


    Private Shared ReadOnly CompactorLogger As ILogger = Application.GetService(Of ILogger(Of Compactor))()
    Private Shared ReadOnly UncompactorLogger As ILogger = Application.GetService(Of ILogger(Of Uncompactor))()
    Private Shared ReadOnly AnalyserLogger As ILogger = Application.GetService(Of ILogger(Of Analyser))()

    Private ReadOnly _resumeService As CompressionResumeService
    Private folderTokens As New Dictionary(Of CompressableFolder, CancellationTokenSource)
    Private ReadOnly stopChoices As New ConcurrentDictionary(Of CompressableFolder, CompressionStopChoice)

    Public Sub New(resumeService As CompressionResumeService)
        _resumeService = resumeService
    End Sub


    Public Async Function CompressFolder(folder As CompressableFolder) As Task(Of CompressionRunResult)
        folder.Compressor = New Compactor(folder.FolderName, WOFHelper.WOFConvertCompressionLevel(folder.CompressionOptions.SelectedCompressionMode), GetSkipList(folder), folder.Analyser, CompactorLogger)
        Return Await RunCompressionAsync(folder, folder.Compressor, Nothing, True)
    End Function


    Public Async Function UncompressFolder(folder As CompressableFolder) As Task(Of Boolean)

        _resumeService.RemoveSession(folder.FolderName)
        folder.Compressor = New Uncompactor(UncompactorLogger)
        Dim compressedFilesList = folder.AnalysisResults.Where(Function(rs) rs.CompressedSize < rs.UncompressedSize).Select(Of String)(Function(f) f.FileName).ToList
        Dim result = Await RunCompressionAsync(folder, folder.Compressor, compressedFilesList, isCompressing:=False)
        Await AnalyseFolderAsync(folder)
        Return result.Completed
    End Function


    Public Sub RequestCompressionStop(folder As CompressableFolder, choice As CompressionStopChoice)
        If folder Is Nothing OrElse Not TypeOf folder.Compressor Is Compactor Then Return
        If folder.FolderActionState <> ActionState.Working AndAlso folder.FolderActionState <> ActionState.Paused Then Return

        If choice = CompressionStopChoice.SaveProgress Then
            _resumeService.SaveSession(folder)
        Else
            _resumeService.RemoveSession(folder.FolderName)
        End If

        stopChoices(folder) = choice

        Try
            folder.Compressor.Cancel()
        Catch ex As ObjectDisposedException
            'The compression completed while the stop dialog was open.
        End Try
    End Sub

    Private Async Function RunCompressionAsync(folder As CompressableFolder, compressor As ICompressor, filesList As List(Of String), isCompressing As Boolean) As Task(Of CompressionRunResult)
        folder.FolderActionState = ActionState.Working

        CancelEstimation(folder)
        Dim cts = New CancellationTokenSource()
        folderTokens(folder) = cts
        Dim progress As IProgress(Of CompressionProgress) = New Progress(Of CompressionProgress)(Sub(x) folder.CompressionProgress = x)
        Dim runResult As New CompressionRunResult()

        progress.Report(New CompressionProgress(0, ""))

        Try
            runResult.Completed = Await compressor.RunAsync(filesList, progress, GetThreadCount(folder))
            runResult.HadWork = Not isCompressing OrElse Not TypeOf compressor Is Compactor OrElse DirectCast(compressor, Compactor).WorkItemCount > 0

            If isCompressing Then
                Dim stopChoice As CompressionStopChoice
                If Not runResult.Completed AndAlso stopChoices.TryRemove(folder, stopChoice) Then
                    runResult.StopChoice = stopChoice

                    If stopChoice = CompressionStopChoice.UndoProgress AndAlso TypeOf compressor Is Compactor Then
                        folder.FolderActionState = ActionState.Undoing
                        Await RestoreProcessedFilesAsync(folder, DirectCast(compressor, Compactor), progress)
                    End If
                ElseIf runResult.Completed Then
                    stopChoices.TryRemove(folder, stopChoice)
                    _resumeService.RemoveSession(folder.FolderName)
                End If

                folder.FolderActionState = ActionState.Results
                folder.IsFreshlyCompressed = runResult.Completed AndAlso runResult.HadWork
            Else
                folder.FolderActionState = ActionState.Idle
                folder.IsFreshlyCompressed = False
            End If

            Return runResult
        Finally
            compressor.Dispose()

            Dim ownedToken As CancellationTokenSource = Nothing
            If folderTokens.TryGetValue(folder, ownedToken) AndAlso Object.ReferenceEquals(ownedToken, cts) Then
                folderTokens.Remove(folder)
                ownedToken.Dispose()
            Else
                cts.Dispose()
            End If
        End Try
    End Function



    Private Async Function RestoreProcessedFilesAsync(folder As CompressableFolder, compactor As Compactor, progress As IProgress(Of CompressionProgress)) As Task
        Dim processedFiles = compactor.ProcessedFiles.ToList()
        If processedFiles.Count = 0 Then Return

        Dim threadCount = GetThreadCount(folder)

        For Each modeGroup In processedFiles.GroupBy(Function(item) item.Value)
            Dim files = modeGroup.Select(Function(item) item.Key).ToList()

            Select Case modeGroup.Key
                Case WOFCompressionAlgorithm.XPRESS4K, WOFCompressionAlgorithm.XPRESS8K, WOFCompressionAlgorithm.XPRESS16K, WOFCompressionAlgorithm.LZX
                    Using restorer As New Compactor(folder.FolderName, modeGroup.Key, Array.Empty(Of String), folder.Analyser, CompactorLogger)
                        Await restorer.RunAsync(files, progress, threadCount)
                    End Using
                Case Else
                    Using uncompactor As New Uncompactor(UncompactorLogger)
                        Await uncompactor.RunAsync(files, progress, threadCount)
                    End Using
            End Select
        Next
    End Function

    Public Async Function AnalyseFolderAsync(folder As CompressableFolder) As Task(Of Integer)

        folder.FolderActionState = ActionState.Analysing
        CancelEstimation(folder)

        Dim cts = New CancellationTokenSource()
        folderTokens(folder) = cts
        Dim token = cts.Token


        folder.Analyser?.Dispose()
        folder.Analyser = New Analyser(folder.FolderName, AnalyserLogger)

        If Not Core.SharedMethods.HasDirectoryWritePermission(folder.FolderName) Then
            folder.FolderActionState = ActionState.Idle
            Return -1
        End If

        Dim retAnalysisResults = Await folder.Analyser.GetAnalysedFilesAsync(token)
        If cts.IsCancellationRequested Then
            folder.FolderActionState = ActionState.Idle
            Return 1
        End If

        folder.AnalysisResults = New ObservableCollection(Of AnalysedFileDetails)(retAnalysisResults)
        folder.UncompressedBytes = folder.Analyser.UncompressedBytes
        folder.CompressedBytes = folder.Analyser.CompressedBytes

        If folder.Analyser.ContainsCompressedFiles OrElse folder.IsFreshlyCompressed Then
            folder.FolderActionState = ActionState.Results
        Else
            folder.FolderActionState = ActionState.Idle
        End If
        folder.PoorlyCompressedFiles = folder.Analyser.GetPoorlyCompressedExtensions()

        Return 0

    End Function

    Public Overridable Async Function GetEstimatedCompression(folder As CompressableFolder) As Task
        folder.IsGettingEstimate = True

        CancelEstimation(folder)
        Dim cts = New CancellationTokenSource()
        folderTokens(folder) = cts

        Dim estimator As New Estimator
        Dim estimatedData As List(Of (AnalysedFile As AnalysedFileDetails, CompressionRatio As Single)) = Nothing

        Try
            estimatedData = Await Task.Run(Function() estimator.EstimateCompression(folder.AnalysisResults.ToList, IsHDD(folder), GetThreadCount(folder), Core.SharedMethods.GetClusterSize(folder.FolderName), cts.Token))

        Catch ex As AggregateException
            folder.IsGettingEstimate = False
            Return
        End Try

        For Each item In estimatedData
            If item.CompressionRatio >= 0.98 AndAlso item.AnalysedFile.FileName <> "" Then
                folder.WikiPoorlyCompressedFiles.Add(item.AnalysedFile.FileName)
            End If
        Next

        Dim estimatedAfterBytes = estimatedData.Sum(Function(x) x.AnalysedFile.UncompressedSize * x.CompressionRatio)

        'This is absolutely stupid

        Dim X4KResult As New CompressionResult
        X4KResult.CompType = CompressionMode.XPRESS4K
        X4KResult.BeforeBytes = folder.UncompressedBytes
        X4KResult.AfterBytes = Math.Min(estimatedAfterBytes * 1.01, folder.UncompressedBytes)
        X4KResult.TotalResults = 1

        Dim X8KResult As New CompressionResult
        X8KResult.CompType = CompressionMode.XPRESS8K
        X8KResult.BeforeBytes = folder.UncompressedBytes
        X8KResult.AfterBytes = Math.Min(estimatedAfterBytes * 1.0, folder.UncompressedBytes)
        X8KResult.TotalResults = 1

        Dim X16KResult As New CompressionResult
        X16KResult.CompType = CompressionMode.XPRESS16K
        X16KResult.BeforeBytes = folder.UncompressedBytes
        X16KResult.AfterBytes = Math.Min(estimatedAfterBytes * 0.98, folder.UncompressedBytes)
        X16KResult.TotalResults = 1

        Dim LZXResult As New CompressionResult
        LZXResult.CompType = CompressionMode.LZX
        LZXResult.BeforeBytes = folder.UncompressedBytes
        LZXResult.AfterBytes = Math.Min(estimatedAfterBytes * 0.95, folder.UncompressedBytes)
        LZXResult.TotalResults = 1

        folder.WikiCompressionResults = New WikiCompressionResults(New List(Of CompressionResult) From {X4KResult, X8KResult, X16KResult, LZXResult})

        folder.IsGettingEstimate = False


        folder.NotifyPropertyChanged(NameOf(folder.WikiCompressionResults))
        folder.NotifyPropertyChanged(NameOf(folder.WikiPoorlyCompressedFiles))
        folder.NotifyPropertyChanged(NameOf(folder.WikiPoorlyCompressedFilesCount))
        folder.NotifyPropertyChanged(NameOf(folder.IsGettingEstimate))

    End Function
    Public Sub CancelEstimation(folder As CompressableFolder)
        If folderTokens.ContainsKey(folder) AndAlso Not folderTokens(folder).IsCancellationRequested Then
            folderTokens(folder).Cancel()
        End If
    End Sub


    Public Shared Function GetThreadCount(folder As CompressableFolder) As Integer
        Dim threadCount As Integer = Application.GetService(Of ISettingsService).AppSettings.MaxCompressionThreads
        If Application.GetService(Of ISettingsService).AppSettings.LockHDDsToOneThread Then
            Dim HDDType As DiskDetector.Models.HardwareType = GetDiskType(folder)
            If HDDType = DiskDetector.Models.HardwareType.Hdd Then
                threadCount = 1
            End If
        End If
        Return threadCount
    End Function

    Public Shared Function GetDiskType(folder As CompressableFolder) As DiskDetector.Models.HardwareType
        If folder.FolderName Is Nothing Then Return DiskDetector.Models.HardwareType.Unknown
        Try
            Return DiskDetector.Detector.DetectDrive(folder.FolderName.First, DiskDetector.Models.QueryType.RotationRate).HardwareType
        Catch ex As Exception
            Return DiskDetector.Models.HardwareType.Unknown
        End Try
    End Function

    Public Shared Function IsHDD(folder As CompressableFolder) As Boolean
        Dim HDDType As DiskDetector.Models.HardwareType = GetDiskType(folder)
        Return HDDType = DiskDetector.Models.HardwareType.Hdd
    End Function

    Private Function GetSkipList(folder As CompressableFolder) As String()
        Dim exclist As String() = Array.Empty(Of String)()

        If folder.CompressionOptions.SkipPoorlyCompressedFileTypes AndAlso Application.GetService(Of ISettingsService).AppSettings.NonCompressableList.Count <> 0 Then
            'Debug.WriteLine("Adding non-compressable list to exclusion list")
            exclist = exclist.Union(Application.GetService(Of ISettingsService).AppSettings.NonCompressableList).ToArray
        End If
        If folder.CompressionOptions.SkipUserSubmittedFiletypes AndAlso folder.WikiPoorlyCompressedFiles?.Count <> 0 Then
            'Debug.WriteLine("Adding estimator poorly compressed list to exclusion list")
            exclist = exclist.Union(folder.WikiPoorlyCompressedFiles).ToArray
        End If

        Return exclist
    End Function


End Class
