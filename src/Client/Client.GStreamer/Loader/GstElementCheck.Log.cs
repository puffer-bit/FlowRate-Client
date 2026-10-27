using Client.Core.Media;
using Microsoft.Extensions.Logging;
using Client.GStreamer.Video;

namespace Client.GStreamer.Loader;

internal static partial class GstElementCheck
{
    [LoggerMessage(LogLevel.Warning, "Optional GStreamer elements missing: {Elements}")]
    private static partial void LogOptionalMissing(ILogger logger, string elements);

    [LoggerMessage(LogLevel.Information, "{Codec} encoders: {Encoders}")]
    private static partial void LogEncoders(ILogger logger, VideoCodec codec, string encoders);

    [LoggerMessage(LogLevel.Information, "{Codec} decoders: {Decoders}")]
    private static partial void LogDecoders(ILogger logger, VideoCodec codec, string decoders);

    [LoggerMessage(LogLevel.Error, "Required GStreamer elements missing: {Elements}")]
    private static partial void LogRequiredMissing(ILogger logger, string elements);
}
