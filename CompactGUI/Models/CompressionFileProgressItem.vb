Imports System.IO

Imports CommunityToolkit.Mvvm.ComponentModel

Imports CompactGUI.Core

Public Class CompressionFileProgressItem
    Inherits ObservableObject

    Public Sub New(rootPath As String, workItem As CompressionWorkItem)
        FullPath = workItem.FileName
        RelativePath = GetRelativePath(rootPath, workItem.FileName)
        UncompressedSize = workItem.UncompressedSize
    End Sub

    Public ReadOnly Property FullPath As String
    Public ReadOnly Property RelativePath As String
    Public ReadOnly Property UncompressedSize As Long

    <NotifyPropertyChangedFor(NameOf(IsIndeterminate), NameOf(ProgressPercent))>
    <ObservableProperty>
    Private _State As CompressionFileState = CompressionFileState.Queued

    <ObservableProperty>
    Private _CompressedSize As Long? = Nothing

    <ObservableProperty>
    Private _FailureReason As String = Nothing

    Public ReadOnly Property IsIndeterminate As Boolean
        Get
            Return State = CompressionFileState.Processing
        End Get
    End Property

    Public ReadOnly Property ProgressPercent As Integer
        Get
            Select Case State
                Case CompressionFileState.Completed, CompressionFileState.Failed
                    Return 100
                Case Else
                    Return 0
            End Select
        End Get
    End Property

    Private Shared Function GetRelativePath(rootPath As String, fullPath As String) As String
        Try
            Return Path.GetRelativePath(rootPath, fullPath)
        Catch ex As ArgumentException
            Return fullPath
        End Try
    End Function
End Class
