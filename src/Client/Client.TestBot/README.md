# Client.TestBot

A headless console client that joins a FlowRate voice session as a regular user and plays audio
into it: a file in a loop or a sine tone. It is the only way to put test audio into a session; the
server has no built-in test peers.

The bot uses the same code as the desktop client (`CoordinatorSession`, `HybridSession`,
`WebRTCTransport`), so it also exercises the client send path and the real join protocol.

## Requirements

- A running coordinator with at least one voice session.
- GStreamer, loaded the same way as in the desktop client: the bundled runtime from the `gstreamer/`
  folder next to the executable (see `Client.GStreamer/Build/README.md`); Debug builds fall back to
  an installed one.

## Running

```bash
dotnet run --project src/Client/Client.TestBot -- --server 127.0.0.1:26666
```

Without `--file` the bot plays a 440 Hz tone. Stop it with Ctrl+C: every bot leaves the session and
disconnects.

## Options

| Option | Description |
|---|---|
| `--server <host:port>` | Coordinator to connect to. Required. |
| `--password <password>` | Coordinator password. |
| `--session <id\|name>` | Voice session to join, by id or name (case-insensitive). Defaults to the first one. |
| `--create-session <name>` | Join the voice session of that name; the first bot creates it when the server has none, as on a fresh local server. Needs the right to create sessions. |
| `--file <path>` | Audio file to play in a loop: wav, mp3, ogg, flac or raw PCM. |
| `--raw-rate <hz>` | Sample rate of a raw 16-bit PCM file, which has no header. Only with `--file`. |
| `--raw-channels <n>` | Channel count of a raw PCM file. Default: 1. |
| `--tone <hz>` | Sine tone frequency when no file is given; each next bot plays 37 Hz higher, so the tones do not beat. Default: 440. |
| `--volume <0..2>` | Gain before encoding. Default: 1. |
| `--count <n>` | Number of bots, each as its own user (1-1000). Default: 1. |
| `--speakers <n>` | Only the first n bots speak; the rest join with the microphone muted and send nothing, like silent users behind the voice gate. Default: all bots speak. |
| `--mute-interval <sec>` | Mute and unmute every given number of seconds. Applies to the speaking bots only. Default: off. |
| `--screen` | Also share a screen: a moving test picture, drawn by the bot itself. |
| `--screen-interval <sec>` | Stop and start the screen share every given number of seconds. Implies `--screen`. Default: off. |
| `--rejoin-interval <sec>` | Leave the session and join it again every given number of seconds, each time with a new transport. Default: off. |
| `--ramp <ms>` | Pause between the starts of two bots, so that a crowd does not hit the login and join rate limits at once. Default: 0. |
| `--no-voice` | Do not join a voice session: the bots only chat. Needs `--chat-interval`. |
| `--chat-interval <sec>` | Each bot writes a message to the chat every given number of seconds (fractions allowed), give or take a fifth, starting at a random moment. Default: off. |
| `--chat <id\|name>` | Chat to write into. Defaults to the first writable one. |
| `--stats-interval <sec>` | Report the counters of all bots every given number of seconds. Default: 10. |
| `--quiet` | Log only failures and the counters, nothing per bot. |
| `--listen` | Play what the bot hears. By default incoming audio is only received and decoded. |
| `--libwebrtc` | Send and receive the voice over libwebrtc instead of GStreamer, as the experimental setting of the client does; needs the libwebrtc runtime of `Client.LibWebRTC` (committed for linux-x64). Screens are shared and watched as well. |
| `--verbose` | Log everything, including the WebRTC negotiation. |
| `--help` | Show the usage text. |

## Examples

```bash
# One bot with a tone in the first voice session
Client.TestBot --server 127.0.0.1:26666 --tone 440

# Three bots playing a file in the session "Test"
Client.TestBot --server 127.0.0.1:26666 --session "Test" --file Assets/Audio/test_audio_simplicity.mp3 --count 3

# Raw 44.1 kHz PCM, muting every 10 seconds
Client.TestBot --server 127.0.0.1:26666 --file test_audio2.pcm --raw-rate 44100 --mute-interval 10

# Load test: 200 users joining one by one, 10 of them speaking, screens shared and watched,
# a chat message from every bot every 5 seconds, counters only
Client.TestBot --server 127.0.0.1:26666 --count 200 --ramp 300 --speakers 10 --screen --watch \
  --chat-interval 5 --quiet

# Chat only: 300 users writing every 2 seconds
Client.TestBot --server 127.0.0.1:26666 --count 300 --ramp 100 --no-voice --chat-interval 2 --quiet
```

Sample files are in `Assets/Audio`.

## Load testing

Every `--stats-interval` seconds the bots report their counters together:

```
[stats] bots: 200 connected, 200 joined, 10 speaking, 200 sharing, 0 failed; connect 412 ms, join 950 ms on average
[stats] chat: 8000 sent, 0 failed, 1592000 received (40/s, 0/s, 7960/s); delivery p50 35 ms, p95 120 ms, max 410 ms
[stats] video: 1200 frames/s received by all bots
```

- **Chat.** Each message starts with `[load] <send time in ticks>`; the receiving bots take the delivery
  time from it, so it is only exact when all bots run on one machine (or on clocks kept in sync).
  A message is counted as received once per receiving bot, so with N bots the received rate is about
  N - 1 times the sent one.
- **Video.** There is no camera in the client yet: the video load is the screen share (`--screen`,
  a test picture) and watching it (`--watch`).
- **Limits.** The server limits connections and logins per IP (`Gateway:MaxConnectionsPerIp`,
  `RateLimits` in its settings). Raise them for a load test from one machine, and use `--ramp` so
  that the logins and joins do not arrive at once.
- **One process.** Every bot has its own GStreamer pipeline and decodes the audio of all the others,
  so the machine running the bots is often the bottleneck before the server. Watch its CPU, or split
  the bots over several machines (each with its own `--count`; identities are numbered per process,
  so give each machine its own user folder).

## Behavior

- **Identities.** Each bot keeps its user between runs instead of registering a new one. Identities
  are stored per server and bot number in
  `<LocalApplicationData>/FlowRate/TestBot/identity-<host>-<port>-<n>.txt`. Delete the file to get a
  new user.
- **Certificates.** The bot trusts whatever certificate the server presents, in memory only. It is
  meant for test servers.
- **Audio files.** A file is decoded once to raw 48 kHz mono S16LE and cached in the temp directory
  as `flowrate-audio-<hash>.pcm`; the cache key includes the path, size and modification time. The
  cached file is then played with `multifilesrc loop=true` and split into 20 ms buffers.
- **Video.** The bot decodes every screen shared in the session and reports every five seconds whose
  video arrives, its size and frame rate; with `--screen` that includes the preview of its own picture.
- **Reconnects.** If the transport fails and the session rejoins, the bot starts the voice and the
  screen again.
- **Failures.** One failing bot does not stop the others. The exit code is 0 on success, 1 when a
  bot failed or GStreamer or the input could not be loaded, and 2 on invalid arguments.
