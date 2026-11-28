using System.Globalization;
using System.Collections.Concurrent;
using Client.Core.Settings;
using Client.Core.Media;
using Gst;
using Gst.GObject;

namespace Client.GStreamer.Video;

internal static class CodecParameters
{
    private static readonly ConcurrentDictionary<string, IReadOnlyList<CodecParameter>> Cache = new();

    public static IReadOnlyList<CodecParameter> Get(string factory) =>
        !MediaRuntime.IsEnabled ? [] : Cache.GetOrAdd(factory, ReadParameters);

    public static IReadOnlyList<CodecPropertyOverride> GetOverrides(string factory,
        IReadOnlyList<CodecPropertyOverride> saved) => saved.Any(value => value.Factory == factory)
            ? CodecParameter.ValidateOverrides(factory, Get(factory), saved)
            : [];

    public static void Apply(Element element, string factory, IReadOnlyList<CodecPropertyOverride> saved)
    {
        foreach (var property in GetOverrides(factory, saved))
            Global.UtilSetObjectArg(element, property.Name, property.Value);
    }

    private static IReadOnlyList<CodecParameter> ReadParameters(string factory)
    {
        if ((!VideoEncoderSelector.IsKnownEncoder(factory) && !VideoDecoderSelector.IsKnownDecoder(factory))
            || ElementFactory.Find(factory) == null)
            return [];
        using var element = ElementFactory.Make(factory, null);
        if (element == null)
            return [];
        var result = new List<CodecParameter>();
        foreach (var property in element.ListProperties())
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
                    case ParamSpecEnum p: kind = "enum"; choices = p.Values.Select(v => v.Nick).OfType<string>().ToArray(); break;
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
                result.Add(new CodecParameter(property.Name, property.Blurb ?? property.Nick, defaults, kind, min, max, choices));
            }
        }
        return result.OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
    }

    private static string F<T>(T value) where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);
}
