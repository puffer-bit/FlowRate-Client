using System.Collections.Generic;
using Client.Core.Sessions.Hybrid;

namespace Client.VideoAnalyzersTest.Analyzers;

/// <param name="Framerate">How fast the pattern is fed meanwhile; 0 freezes it.</param>
/// <param name="ClearsPicture">The surface is cleared, as when the decoder fails.</param>
public sealed record IssueScenario(string Name, int Framerate, IReadOnlyList<StreamIssue> Issues, bool ClearsPicture = false);

/// <summary>The states the test cycles through, with figures the analyzer might report.</summary>
public static class IssueScenarios
{
    public const int NormalFramerate = 60;

    public static IReadOnlyList<IssueScenario> All { get; } =
    [
        new("No issues", NormalFramerate, []),
        new("Light packet loss", NormalFramerate,
        [
            new(StreamIssueKind.PacketLoss, StreamIssueSeverity.Warning, 4.2)
        ]),
        new("High jitter", NormalFramerate,
        [
            new(StreamIssueKind.HighJitter, StreamIssueSeverity.Warning, 65)
        ]),
        new("Connection over TCP", NormalFramerate,
        [
            new(StreamIssueKind.TcpTransport, StreamIssueSeverity.Warning)
        ]),
        new("TCP with loss", 45,
        [
            new(StreamIssueKind.TcpTransport, StreamIssueSeverity.Warning),
            new(StreamIssueKind.PacketLoss, StreamIssueSeverity.Warning, 2.8)
        ]),
        new("Heavy loss, frozen picture", 0,
        [
            new(StreamIssueKind.PacketLoss, StreamIssueSeverity.Critical, 18),
            new(StreamIssueKind.WaitingForKeyframe, StreamIssueSeverity.Critical, 4)
        ]),
        new("Streamer sends nothing", 0,
        [
            new(StreamIssueKind.NoSourceFrames, StreamIssueSeverity.Critical, 6)
        ]),
        new("Sound is missing", NormalFramerate,
        [
            new(StreamIssueKind.NoAudio, StreamIssueSeverity.Warning, 4)
        ]),
        new("Rendering falls behind", 24,
        [
            new(StreamIssueKind.RendererBehind, StreamIssueSeverity.Warning, 36, 60)
        ]),
        new("Decoder stalled", 0,
        [
            new(StreamIssueKind.DecoderStalled, StreamIssueSeverity.Critical, 4)
        ], ClearsPicture: true),
        new("Everything at once", 20,
        [
            new(StreamIssueKind.PacketLoss, StreamIssueSeverity.Warning, 6.1),
            new(StreamIssueKind.HighJitter, StreamIssueSeverity.Warning, 40),
            new(StreamIssueKind.TcpTransport, StreamIssueSeverity.Warning),
            new(StreamIssueKind.NoAudio, StreamIssueSeverity.Warning, 6),
            new(StreamIssueKind.RendererBehind, StreamIssueSeverity.Warning, 10, 30)
        ])
    ];
}
