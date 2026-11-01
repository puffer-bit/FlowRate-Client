using System;
using Avalonia.OpenGL;
using Client.Core.Media;
using Microsoft.Extensions.Logging;

namespace Client.Avalonia.Video;

public static partial class VideoSurfaceProbe
{
    [LoggerMessage(LogLevel.Warning, "Failed to find out how video can reach the screen, frames are copied")]
    private static partial void LogProbeFailed(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Information, "Video presenter: {Presenter}, frames without copying: {Kinds}")]
    private static partial void LogProbed(ILogger logger, string presenter, VideoSurfaceKinds kinds);

    [LoggerMessage(LogLevel.Information, "The renderer shares no OpenGL context: {Feature}")]
    private static partial void LogNoSharing(ILogger logger, string feature);

    [LoggerMessage(LogLevel.Information, "The renderer cannot create a shared OpenGL context ({CanCreate})")]
    private static partial void LogNoSharedContext(ILogger logger, bool canCreate);

    [LoggerMessage(LogLevel.Information, "Shared OpenGL context: {Version}, image import {CanImport}")]
    private static partial void LogSharedContext(ILogger logger, GlVersion version, bool canImport);

    [LoggerMessage(LogLevel.Warning, "Direct3D 11 frames cannot be imported: {Reason}")]
    internal static partial void LogD3D11Unsupported(ILogger logger, string? reason);

    [LoggerMessage(LogLevel.Warning, "Direct3D 11 frame import failed: {Reason}")]
    internal static partial void LogD3D11ImportFailed(ILogger logger, string? reason);
}
