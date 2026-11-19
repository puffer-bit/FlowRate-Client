using Client.Application.Ports;
using Client.Core.Media;
using Client.Core.Settings;
using Client.GStreamer.Video;
using Gst;

internal static class CodecSettingsSmokeChecks
{
    public static void Run()
    {
        Console.WriteLine("Checking encoder and decoder parameters...");
        IMediaCapabilities capabilities = new GstMediaCapabilities();
        foreach (var codec in Enum.GetValues<VideoCodec>())
        {
            foreach (var factory in capabilities.GetScreenShareEncoders(codec)
                         .Concat(capabilities.GetScreenShareDecoders(codec)))
            {
                var parameters = capabilities.GetCodecParameters(factory);
                Require(parameters.Count > 0, $"{factory} exposes editable parameters");
                Require(parameters.All(parameter => parameter.Name is not ("name" or "parent")),
                    $"{factory} excludes object identity parameters");
            }
        }
        foreach (var (codec, factory, bitrateProperty, value) in new[]
        {
            (VideoCodec.H264, "openh264enc", "bitrate", "1500000"),
            (VideoCodec.AV1, "svtav1enc", "target-bitrate", "1500"),
            (VideoCodec.VP8, "vp8enc", "target-bitrate", "1500000"),
            (VideoCodec.VP9, "vp9enc", "target-bitrate", "1500000")
        })
        {
            CodecPropertyOverride[] saved = [new(factory, bitrateProperty, value), new(factory, "unknown-property", "1")];
            var choice = VideoEncoderSelector.Select(codec, 1000, factory, saved)!;
            Require(choice.Factory == factory && choice.Properties.Contains((bitrateProperty, value)),
                $"{factory} applies its bitrate override");
            Require(choice.Overrides!.Count == 1, $"{factory} ignores unsupported overrides");
            using var element = choice.CreateElement(null)!;
            using var actual = element.GetProperty(bitrateProperty);
            Require(Global.ValueSerialize(actual) == value, $"{factory} receives the override in GStreamer");
            var updated = VideoEncoderSelector.Select(codec, 2000, factory, choice.Overrides)!;
            Require(updated.Properties.Contains((bitrateProperty, value)), $"{factory} retains the bitrate override");
        }
        foreach (var factory in new[] { "openh264dec", "dav1ddec", "vp8dec", "vp9dec" })
        {
            using var element = ElementFactory.Make(factory, null)!;
            var parameter = capabilities.GetCodecParameters(factory).First(p => p.Name == "max-errors");
            Require(parameter.IsValid("7"), $"{factory} validates decoder parameters");
            CodecParameters.Apply(element, factory, [new(factory, "max-errors", "7")]);
            using var actual = element.GetProperty("max-errors");
            Require(actual.GetInt() == 7, $"{factory} receives decoder parameters");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
