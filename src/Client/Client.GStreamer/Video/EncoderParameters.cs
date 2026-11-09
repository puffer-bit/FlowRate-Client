using System.Globalization;
using Client.Core.Media;
using Client.Core.Settings;
using Gst;
using Gst.GObject;

namespace Client.GStreamer.Video;

internal static class EncoderParameters
{
    public static IReadOnlyList<EncoderParameter> Get(string factory)
    {
        if (!MediaRuntime.IsEnabled || !VideoEncoderSelector.GetKnownEncoders(VideoCodec.AV1, true).Contains(factory)
            || ElementFactory.Find(factory) == null)
            return [];
        using var encoder = ElementFactory.Make(factory, null);
        if (encoder == null)
            return [];
        var result = new List<EncoderParameter>();
        foreach (var property in encoder.ListProperties())
        {
            using (property)
            {
                if ((property.Flags & (ParamFlags.Readable | ParamFlags.Writable)) != (ParamFlags.Readable | ParamFlags.Writable)
                    || (property.Flags & ParamFlags.ConstructOnly) != 0 || property.Name is "name" or "parent")
                    continue;
                using var value = property.DefaultValue.ToValue();
                var defaults = Global.ValueSerialize(value) ?? "";
                string? min = null, max = null;
                string kind;
                IReadOnlyList<string>? choices = null;
                switch (property)
                {
                    case ParamSpecBoolean: kind = "boolean"; choices = ["false", "true"]; break;
                    case ParamSpecEnum p: kind = "enum"; choices = p.Values.Select(v => v.Nick).ToArray(); break;
                    case ParamSpecInt p: kind = "integer"; min = F(p.Minimum); max = F(p.Maximum); break;
                    case ParamSpecUInt p: kind = "integer"; min = F(p.Minimum); max = F(p.Maximum); break;
                    case ParamSpecInt64 p: kind = "integer"; min = F(p.Minimum); max = F(p.Maximum); break;
                    case ParamSpecUInt64 p: kind = "integer"; min = F(p.Minimum); max = F(p.Maximum); break;
                    case ParamSpecLong p: kind = "integer"; min = F(p.Minimum); max = F(p.Maximum); break;
                    case ParamSpecULong p: kind = "integer"; min = F(p.Minimum); max = F(p.Maximum); break;
                    case ParamSpecFloat p: kind = "number"; min = F(p.Minimum); max = F(p.Maximum); break;
                    case ParamSpecDouble p: kind = "number"; min = F(p.Minimum); max = F(p.Maximum); break;
                    default: continue;
                }
                result.Add(new EncoderParameter(property.Name, property.Blurb ?? property.Nick, defaults, kind, min, max, choices));
            }
        }
        return result.OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
    }

    private static string F<T>(T value) where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);
}
