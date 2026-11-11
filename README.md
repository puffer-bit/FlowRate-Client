> [!WARNING]
> FlowRate Client is still under development. Bugs, incomplete features, and breaking changes may occur.

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="src/Client/Client.Avalonia.Assets/AppIcons/Headphones/svg/wordmark-on-dark.svg">
    <source media="(prefers-color-scheme: light)" srcset="src/Client/Client.Avalonia.Assets/AppIcons/Headphones/svg/wordmark-on-light.svg">
    <img src="src/Client/Client.Avalonia.Assets/AppIcons/Headphones/svg/wordmark-on-light.svg" alt="FlowRate" width="720">
  </picture>
</p>

# FlowRate Client

Voice, screen sharing, and chat for communities running their own FlowRate server.

FlowRate Client is a desktop application for Windows and Linux. Connect to your community’s server to exchange messages, join voice sessions, and share your screen.

You need a running FlowRate server to connect. Ask your community administrator for its address and any required password.

[Download builds](https://github.com/puffer-bit/FlowRate-Client/releases) · [Report an issue](https://github.com/puffer-bit/FlowRate-Client/issues) · [Third-party notices](THIRD-PARTY-NOTICES.md)

## Contents

- [What you can do](#what-you-can-do)
- [Platforms and packages](#platforms-and-packages)
- [Getting started](#getting-started)
- [Chat and formatting](#chat-and-formatting)
- [Settings and personalization](#settings-and-personalization)
- [Local data and protection](#local-data-and-protection)
- [Desktop launch options](#desktop-launch-options)
- [Troubleshooting](#troubleshooting)
- [License](#license)

## What you can do

### Voice and screen sharing

- Join voice sessions, mute your microphone, deafen playback, and adjust individual participants' volume.
- Make one-to-one audio calls from personal chats. Incoming calls appear in FlowRate notifications with Accept and Decline buttons. Offline Android recipients receive an encrypted push call notification when FCM or the push relay is configured and their device is registered. Only the recipient hears the repeating ringing signal, until the call is answered, declined, cancelled or times out. Calls use the existing microphone and playback controls, and store their outcome and duration in chat history. Client and coordinator must both support protocol 6.
- Configure microphone and output devices, voice gating, noise suppression, echo cancellation, and automatic gain control.
- Use desktop global hotkeys for common voice actions.
- Share a screen or window from desktop, with audio where supported by the capture backend.
- Watch screen shares on demand and adjust screen sharing and video settings.
- Use hardware video paths where the operating system, driver, codec, and capture backend support them.

Webcam calling is not currently implemented.

Session settings can select AV1 for screen sharing when the server and all viewers support it;
H.264 remains the default. AV1 requires an updated coordinator and SFU. Desktop builds using
libwebrtc also need native libraries rebuilt from the updated sources on each platform; older
bundled binaries support H.264 and VP8 only. See [video codecs](src/Client/Client.GStreamer/Docs/VideoCodecs.md)
for runtime requirements and validation commands. Android viewing depends on the device's available decoders.

### Chat and community tools

- Exchange messages with Markdown formatting, mentions, images, and file attachments.
- Play supported audio and video attachments inside the client when media is enabled.
- Edit messages while preserving their original Markdown.
- Track unread messages and mute individual chats.
- Create personal conversations and groups within a server. Switch between server channels and personal chats from the server name menu; the existing create-session action follows the selected list.
- See the latest message in the personal chat list. Group owners can rename groups, manage participants, and transfer ownership; other participants can leave.
- Manage sessions, roles, and moderation actions according to the permissions granted by the server.
- Keep saved server identities and local user notes.

### Everyday use

- Switch between English, Russian, Spanish, Japanese, and Chinese.
- Customize appearance, notification sounds, and application behavior.
- Protect the local database with optional encryption and, where available, store its password in the system keyring.
- Receive desktop updates through installed Velopack packages, with separate release and development channels.
- Start with temporary diagnostic options without changing saved preferences.

## Platforms and packages

The packaging scripts target the following platforms:

| Platform | Package forms | Notes |
|---|---|---|
| Windows x64 | `Setup.exe`, standalone `.zip` | Installed packages support automatic updates. |
| Linux x64 | `.AppImage`, standalone `.tar.gz` | Installed packages support automatic updates. |

Choose an asset for your platform from [GitHub Releases](https://github.com/puffer-bit/FlowRate-Client/releases). Available assets depend on the release. Desktop JIT and NativeAOT variants use separate update channels.

Desktop release packages include the .NET runtime or a native executable, together with their media libraries. End users do not need to install .NET or GStreamer separately. Keep the accompanying libraries and runtime folders alongside the executable when unpacking a standalone archive.

The Linux GStreamer bundle is built against glibc 2.35. Its documented baseline includes Ubuntu 22.04+ and Debian 12+. It still depends on the host's graphics drivers and relevant system audio libraries; see the [runtime compatibility notes](src/Client/Client.GStreamer/Build/README.md#compatibility).

Standalone archives do not update themselves. Replace them manually with a newer archive. Application files and user data are stored separately, including when using an installed package.

## Getting started

1. Download and install or extract the package for your platform.
2. Launch FlowRate and open the connection dialog.
3. Enter the server address supplied by your administrator. The client's default coordinator port is `26666`; use the server's configured port if it differs.
4. Select a saved identity or create a new one, and provide a server password if required.
5. Open a chat or join an available voice session. Session creation and administrative actions depend on your server permissions.
6. Check microphone and playback devices in Settings before speaking.

For a server running on the same machine with the default port, the address is `127.0.0.1:26666`.

## Chat and formatting

The message editor supports these Markdown forms:

| Formatting | Syntax | Desktop shortcut |
|---|---|---|
| Bold | `**text**` | `Ctrl+B` |
| Italic | `*text*` | `Ctrl+I` |
| Strikethrough | `~~text~~` | `Ctrl+Shift+X` |
| Inline code | `` `code` `` | `Ctrl+E` |
| Fenced code block | Three backticks around a block | `Ctrl+Shift+E` |
| Link | `[label](https://example.com)` | `Ctrl+K` |

The input flyout exposes the same formatting commands with icons. Commands wrap the selection or insert delimiters at the caret when nothing is selected. The link command selects the destination so you can replace it immediately.

Other Markdown block syntax remains plain text. Messages retain their source Markdown for editing, while selection and copying use the displayed text. Code backgrounds, text, and icons follow the active theme, including theme changes while chat is open.

With `--no-media`, audio and video attachments appear as files to download or open externally instead of playing inside the client.

## Settings and personalization

Settings cover appearance, audio, video, screen sharing, hotkeys, behavior, storage, saved accounts, experimental features, and debug logs.

Use appearance settings to adjust the theme and chat presentation. Audio settings separate voice devices and processing from interface sounds. Storage settings manage locally cached media; saved identities and database protection are managed separately from disposable media cache.

Experimental settings include the desktop GStreamer transport fallback. When diagnosing a media problem, record which transport was selected and whether a fallback occurred; the journal records the engine used for a session.

Options supplied on the command line apply to the current process. Affected settings are disabled in the interface for that launch. The settings header indicates safe mode, disabled media, and software rendering where applicable.

## Local data and protection

### Data directory

The desktop data directory contains `client.db`, logs, media cache, sound packs, and other persistent local state.

| Operating system | Default location |
|---|---|
| Windows | `%LocalAppData%\FlowRate` |
| Linux | The .NET `LocalApplicationData` directory plus `FlowRate`, normally `~/.local/share/FlowRate` |

Choose another directory with `--data-dir`:

```powershell
.\Client.Avalonia.Desktop.exe --data-dir "D:\FlowRateData"
```

Relative paths resolve against the working directory. Missing directories are created, and startup checks that the directory is writable. An invalid or unwritable path stops startup with exit code `1`.

Selecting a different directory does not move existing data. Supply the option on each launch that should use it. Close the client before copying its database or moving the data directory.

### Database encryption

Database encryption is configurable in Settings; it is not a claim that every local file is encrypted. The database implementation uses SQLite3 Multiple Ciphers with ChaCha20-Poly1305. Password storage in the system keyring depends on platform availability. Without a stored password, an encrypted database needs to be unlocked at startup.

Database encryption protects local database contents. It does not encrypt downloaded attachments, logs, or sound packs, and does not imply end-to-end encryption of conversations. Keep access to your database password when backing up encrypted data; copying the database alone does not transfer a password stored in another machine's keyring.

### Multiple instances

By default, a second desktop launch activates the running application. To allow separate processes, start the **first** process with `--allow-multiple-instances`.

For independent test clients, give each process a separate `--data-dir`. Allowing another process to start does not make sharing a database safe or bypass database locks.

## Desktop launch options

| Option | Behavior |
|---|---|
| `--data-dir PATH` | Use a custom data directory. |
| `--minimized` | Start in the tray after initialization; unlock and recovery prompts remain visible. |
| `--allow-multiple-instances` | Allow subsequent launches to open separate instances; set this on the first process. |
| `--no-media` | Disable voice and video media while keeping OpenAL interface sounds available. |
| `--safe-mode` | Disable media, global hotkeys, automatic connections, and session restoration. |
| `--no-auto-connect` | Skip automatic connections and session restoration; manual connections remain available. |
| `--disable-updates` | Disable update checks and controls for this launch. |
| `--no-file-logging` | Disable file logging; keep the in-memory journal. |
| `--log-level LEVEL` | Set `verbose`, `debug`, `information`, `warning`, `error`, or `fatal`; `info` and `warn` are aliases. |
| `--software-rendering` | Render the application interface in software. |
| `--lang CODE` | Override the interface language with `en`, `ru`, `es`, `ja`, or `zh`. |
| `--help`, `-h` | Print help and exit before opening application data. |
| `--version` | Print the application version and exit before opening application data. |

Value options also accept `--name=value`. Options can be combined. Run the examples below from the extracted application directory:

```powershell
.\Client.Avalonia.Desktop.exe --safe-mode --lang en --log-level debug
```

```bash
./Client.Avalonia.Desktop --no-auto-connect --software-rendering
```

Unknown arguments, repeated options, and invalid or missing values stop startup with exit code `1`. Overrides do not overwrite saved preferences. An application-triggered restart after an update preserves launch options and the absolute data path.

`--no-media` avoids loading GStreamer and libwebrtc. Microphone and video settings are unavailable, voice sessions cannot be joined or restored, and chat media uses downloadable files. Restart without the option to restore media.

Safe mode does not automatically enable software rendering; combine `--safe-mode --software-rendering` when investigating both startup and graphics problems.

## Troubleshooting

| Symptom | What to check |
|---|---|
| A second launch opens the existing window | This is normal single-instance behavior. Start the first process with `--allow-multiple-instances` for separate test processes. |
| Startup fails before the main window | Check data-directory permissions and command-line values. Try `--safe-mode`; add `--software-rendering` if graphics initialization is involved. |
| Voice or video is unavailable | Remove `--no-media` or `--safe-mode`, check device permissions, and inspect media runtime errors in the journal. |
| Screen sharing fails on Linux | Check PipeWire, the appropriate xdg-desktop-portal backend, capture permissions, graphics drivers, and required GStreamer elements. |
| Software rendering changes UI behavior but capture still fails | `--software-rendering` controls the application interface; media capture and encoding have separate platform requirements. |
| An extracted archive does not update | Standalone archives require manual replacement; use an installed package for automatic updates. |
| An encrypted database cannot be opened | Verify its password and keyring availability, and check that another process is not holding the database. |

For a useful issue report, include the client build, OS, package type, reproduction steps, launch options, selected media transport, and relevant journal output. Review logs for private server addresses, identities, or message content before sharing them.

For source builds, tests, and contribution instructions, see the [development guide](DEVELOPMENT.md).

## License

FlowRate Client is licensed under the [MIT License](LICENSE). Bundled libraries retain their own licenses and distribution requirements; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and the notices shipped alongside release packages.
