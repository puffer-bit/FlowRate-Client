# Functional walkthrough

This walkthrough checks chat, voice, screen sharing, and SFU recovery using two desktop clients.

## Prepare the environment

Use a dedicated test server and test identities, messages, and attachments. Record the client and server tags or commits used for the check.

Start the test server. For a Linux-only local Docker setup, configure the advertised address for the actual client location; `127.0.0.1` is suitable only when the clients run on that same host. Check the configured client and media ports. Native Docker host networking and remote clients require the corresponding host/network setup.

Use packaged desktop builds with their media runtimes, or follow the [source build instructions](../../DEVELOPMENT.md#building-from-source).

Close any existing FlowRate process before starting the two instances. From the client repository root, launch these commands in separate terminals:

```bash
# Terminal 1
dotnet run --project src/Client/Client.Avalonia.Desktop -- --allow-multiple-instances --data-dir /tmp/flowrate-test-alice --disable-updates --no-auto-connect
```

```bash
# Terminal 2
dotnet run --project src/Client/Client.Avalonia.Desktop -- --allow-multiple-instances --data-dir /tmp/flowrate-test-bob --disable-updates --no-auto-connect
```

These commands are for Linux. For a packaged executable, pass the same application arguments directly to it; on Windows, use two distinct writable data-directory paths. Keep separate databases for the two processes. The first running instance must allow multiple instances. The directories persist between launches; these commands do not reset identities or server data.

Connect both clients to the test server, create two identities, and prepare one writable chat and one voice session. Give each identity the permissions required for the checks. Check microphone and output devices, capture permissions, and the screen-sharing backend. Use headphones and mute the second local microphone to avoid feedback. A second device or the test bot can help check received speech.

## Check the main flows

| Action | Expected behavior |
|---|---|
| Send a Markdown message and a small image, then edit the message. | The second client receives the message, attachment, and edit. The editor preserves the original Markdown. |
| Join a voice session from both clients. | Both participants appear in the session and can receive audio with working devices and permissions. |
| Toggle mute and adjust participant volume. | Mute state is reflected in the interface; volume changes affect local playback. |
| Share a window from the first client and watch it from the second. | The second client displays the selected stream. Capture availability depends on the OS, permissions, and media backend. |
| Stop sharing. | The stream ends and the second client updates its viewing state. |

See the [architecture guide](architecture.md) for the WebSocket control, gRPC SFU management, and WebRTC media paths involved in these flows.

## Check SFU recovery

On a dedicated deployment with a single SFU, run `docker compose stop mediasoup` from the server repository. Observe the waiting state while the coordinator remains running, then run `docker compose start mediasoup` and check that media can reconnect. A media pause is expected.

Checking reassignment requires two configured SFUs assigned to the coordinator. Prepare that topology separately. Record the observed recovery behavior and timing for the exact versions used.

## Record results

Record the date, OS, client and server revisions, SFU version, package type, and selected media transport. For each check, note the observed result and any relevant journal output. Include recovery timing when testing SFU interruption.

Keep runtime observations separate from automated test results. A successful managed build alone does not verify microphone access, audio playback, or screen capture.
