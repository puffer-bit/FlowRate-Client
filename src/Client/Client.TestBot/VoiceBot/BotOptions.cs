using System.Globalization;

namespace Client.TestBot;

/// <summary>
/// Command line of the test bot.
/// </summary>
internal sealed record BotOptions
{
    public const string Usage = """
        FlowRate test bot: joins a voice session like a regular user and plays audio into it.

        Usage:
          Client.TestBot --server <host:port> [options]

        Options:
          --server <host:port>     Coordinator to connect to (required).
          --password <password>    Coordinator password.
          --session <id|name>      Voice session to join. Defaults to the first one on the server.
          --file <path>            Audio file to play in a loop: wav, mp3, ogg, flac or raw PCM.
          --raw-rate <hz>          Sample rate of a raw 16-bit PCM file, which carries no header.
          --raw-channels <n>       Channel count of a raw PCM file. Default: 1.
          --tone <hz>              Play a sine tone instead of a file. Default when no file: 440.
                                   Each next bot plays 37 Hz higher, so the tones do not beat.
          --volume <0..2>          Gain before encoding. Default: 1.
          --count <n>              Number of bots to run, each as its own user (1-1000). Default: 1.
          --ramp <ms>              Pause between the starts of two bots, so that a crowd does not
                                   hit the login and join rate limits at once. Default: 0.
          --speakers <n>           Only the first n bots speak; the rest join with the microphone
                                   muted and send nothing, like silent users behind the voice gate.
                                   Default: all bots speak.
          --mute-interval <sec>    Mute and unmute every given number of seconds. Applies to the
                                   speaking bots only. Default: off.
          --screen                 Also share a screen: a moving test picture.
          --screen-interval <sec>  Stop and start the screen share every given number of seconds.
                                   Implies --screen. Default: off.
          --screen-fps <n>         Frame rate of the shared screen, one the dialog of the client
                                   offers (15, 30, 60, 144). Implies --screen. Default: 30.
          --screen-audio           Send a tone along with the shared screen. Implies --screen.
          --watch                  Watch every screen that is shared. By default none is received.
          --watch-interval <sec>   Stop and start watching every given number of seconds.
                                   Implies --watch. Default: off.
          --gpu-frames             Take the frames of watched screens the way a screen that draws
                                   with the GPU does (DMA-BUF, CUDA, Direct3D 11), to exercise those
                                   decoding branches. The frames are only counted.
          --gpu-frames-fail <sec>  With --gpu-frames: report after the given number of seconds that
                                   GPU frames cannot be shown, as a screen does when the import
                                   fails, so the branches are rebuilt to deliver frames in memory.
          --rejoin-interval <sec>  Leave the session and join it again every given number of seconds.
          --no-voice               Do not join a voice session: chat only.
          --chat-interval <sec>    Each bot writes a message to the chat every given number of
                                   seconds (fractions allowed), give or take a fifth. Default: off.
          --chat <id|name>         Chat to write into. Defaults to the first writable one.
          --stats-interval <sec>   Report the counters of all bots every given number of seconds:
                                   connections, joins, chat sent/received and delivery time,
                                   received video frames. Default: 10.
          --quiet                  Only failures and the counters, nothing per bot.
          --listen                 Play what the bot hears. By default incoming audio is only
                                   received and decoded, so several bots do not fill the speakers.
          --verbose                Log everything, including the WebRTC negotiation.
          --help                   Show this text.

        Each bot keeps its user between runs; the identities are stored in
        <local application data>/FlowRate/TestBot.

        Examples:
          Client.TestBot --server 127.0.0.1:26666 --tone 440
          Client.TestBot --server 127.0.0.1:26666 --session "Test" --file music.mp3 --count 3
          Client.TestBot --server 127.0.0.1:26666 --file test_audio2.pcm --raw-rate 44100
          Client.TestBot --server 127.0.0.1:26666 --count 90 --speakers 3
          Client.TestBot --server 127.0.0.1:26666 --count 200 --ramp 300 --speakers 10 \
            --screen --watch --chat-interval 5 --quiet
        """;

    public required string Host { get; init; }
    public required int Port { get; init; }
    public string? Password { get; init; }
    public string? Session { get; init; }
    public string? File { get; init; }
    public int? RawSampleRate { get; init; }
    public int RawChannels { get; init; } = 1;
    public double ToneFrequency { get; init; } = 440;
    public double Volume { get; init; } = 1;
    public int Count { get; init; } = 1;
    public int? Speakers { get; init; }
    public TimeSpan? MuteInterval { get; init; }
    public bool Verbose { get; init; }
    public bool Listen { get; init; }
    public bool Screen { get; init; }
    public TimeSpan? ScreenInterval { get; init; }
    public int ScreenFramerate { get; init; } = Client.Core.Media.ScreenShareSettings.DefaultFramerate;
    public bool ScreenAudio { get; init; }
    public bool GpuFrames { get; init; }
    public TimeSpan? GpuFramesFailAfter { get; init; }
    public bool Watch { get; init; }
    public TimeSpan? WatchInterval { get; init; }
    public TimeSpan? RejoinInterval { get; init; }
    public TimeSpan Ramp { get; init; }
    public bool Voice { get; init; } = true;
    public string? Chat { get; init; }
    public TimeSpan? ChatInterval { get; init; }
    public TimeSpan StatsInterval { get; init; } = TimeSpan.FromSeconds(10);
    public bool Quiet { get; init; }

    /// <returns>The options, or null when help was requested.</returns>
    /// <exception cref="ArgumentException">An option is missing or malformed.</exception>
    public static BotOptions? Parse(IReadOnlyList<string> args)
    {
        string? server = null, password = null, session = null, file = null;
        int? rawRate = null, speakers = null, muteSeconds = null, screenSeconds = null, rejoinSeconds = null, watchSeconds = null;
        var watch = false;
        int rawChannels = 1, count = 1;
        double tone = 440, volume = 1;
        var verbose = false;
        var listen = false;
        var screen = false;
        var screenAudio = false;
        var screenFramerate = Client.Core.Media.ScreenShareSettings.DefaultFramerate;
        var gpuFrames = false;
        int? gpuFailSeconds = null;
        var voice = true;
        var quiet = false;
        string? chat = null;
        double? chatSeconds = null;
        int ramp = 0, statsSeconds = 10;

        for (var i = 0; i < args.Count; i++)
        {
            var name = args[i];

            string Value()
            {
                if (i + 1 >= args.Count)
                    throw new ArgumentException($"{name} needs a value");

                return args[++i];
            }

            switch (name)
            {
                case "--help" or "-h":
                    return null;
                case "--server":
                    server = Value();
                    break;
                case "--password":
                    password = Value();
                    break;
                case "--session":
                    session = Value();
                    break;
                case "--file":
                    file = Value();
                    break;
                case "--raw-rate":
                    rawRate = ParseInt(name, Value(), 8000, 192000);
                    break;
                case "--raw-channels":
                    rawChannels = ParseInt(name, Value(), 1, 8);
                    break;
                case "--tone":
                    tone = ParseDouble(name, Value(), 20, 20000);
                    break;
                case "--volume":
                    volume = ParseDouble(name, Value(), 0, 2);
                    break;
                case "--count":
                    count = ParseInt(name, Value(), 1, 1000);
                    break;
                case "--speakers":
                    speakers = ParseInt(name, Value(), 0, 1000);
                    break;
                case "--mute-interval":
                    muteSeconds = ParseInt(name, Value(), 1, 3600);
                    break;
                case "--rejoin-interval":
                    rejoinSeconds = ParseInt(name, Value(), 1, 3600);
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--listen":
                    listen = true;
                    break;
                case "--screen":
                    screen = true;
                    break;
                case "--screen-interval":
                    screenSeconds = ParseInt(name, Value(), 1, 3600);
                    screen = true;
                    break;
                case "--screen-fps":
                    screenFramerate = ParseInt(name, Value(), 1, 240);
                    screen = true;
                    break;
                case "--screen-audio":
                    screenAudio = true;
                    screen = true;
                    break;
                case "--gpu-frames":
                    gpuFrames = true;
                    break;
                case "--gpu-frames-fail":
                    gpuFailSeconds = ParseInt(name, Value(), 1, 3600);
                    gpuFrames = true;
                    break;
                case "--watch":
                    watch = true;
                    break;
                case "--watch-interval":
                    watchSeconds = ParseInt(name, Value(), 1, 3600);
                    watch = true;
                    break;
                case "--ramp":
                    ramp = ParseInt(name, Value(), 0, 60000);
                    break;
                case "--no-voice":
                    voice = false;
                    break;
                case "--chat":
                    chat = Value();
                    break;
                case "--chat-interval":
                    chatSeconds = ParseDouble(name, Value(), 0.1, 3600);
                    break;
                case "--stats-interval":
                    statsSeconds = ParseInt(name, Value(), 1, 3600);
                    break;
                case "--quiet":
                    quiet = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option {name}");
            }
        }

        if (server == null)
            throw new ArgumentException("--server is required");

        var separator = server.LastIndexOf(':');

        if (separator <= 0 || !int.TryParse(server[(separator + 1)..], out var port) || port is < 1 or > 65535)
            throw new ArgumentException("--server must look like host:port");

        if (rawRate.HasValue && file == null)
            throw new ArgumentException("--raw-rate only applies to --file");

        if (chat != null && chatSeconds == null)
            throw new ArgumentException("--chat only applies to --chat-interval");

        if (!voice && chatSeconds == null)
            throw new ArgumentException("--no-voice leaves nothing to do without --chat-interval");

        return new BotOptions
        {
            Host = server[..separator],
            Port = port,
            Password = password,
            Session = session,
            File = file,
            RawSampleRate = rawRate,
            RawChannels = rawChannels,
            ToneFrequency = tone,
            Volume = volume,
            Count = count,
            Speakers = speakers,
            MuteInterval = muteSeconds.HasValue ? TimeSpan.FromSeconds(muteSeconds.Value) : null,
            Verbose = verbose,
            Listen = listen,
            Screen = screen,
            ScreenFramerate = screenFramerate,
            ScreenAudio = screenAudio,
            GpuFrames = gpuFrames,
            GpuFramesFailAfter = gpuFailSeconds.HasValue ? TimeSpan.FromSeconds(gpuFailSeconds.Value) : null,
            ScreenInterval = screenSeconds.HasValue ? TimeSpan.FromSeconds(screenSeconds.Value) : null,
            Watch = watch,
            WatchInterval = watchSeconds.HasValue ? TimeSpan.FromSeconds(watchSeconds.Value) : null,
            RejoinInterval = rejoinSeconds.HasValue ? TimeSpan.FromSeconds(rejoinSeconds.Value) : null,
            Ramp = TimeSpan.FromMilliseconds(ramp),
            Voice = voice,
            Chat = chat,
            ChatInterval = chatSeconds.HasValue ? TimeSpan.FromSeconds(chatSeconds.Value) : null,
            StatsInterval = TimeSpan.FromSeconds(statsSeconds),
            Quiet = quiet
        };
    }

    private static int ParseInt(string name, string value, int min, int max)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ||
            result < min || result > max)
            throw new ArgumentException($"{name} must be a whole number from {min} to {max}");

        return result;
    }

    private static double ParseDouble(string name, string value, double min, double max)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ||
            result < min || result > max)
            throw new ArgumentException($"{name} must be a number from {min} to {max}");

        return result;
    }
}
