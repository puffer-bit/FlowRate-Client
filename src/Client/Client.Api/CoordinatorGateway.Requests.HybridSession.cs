using Client.Api.Mappers;
using Shared.Models.Requests.SessionActions.HybridSession;
using Shared.Models.Responses.SessionActions.HybridSession;
using Client.Core.Sessions.Hybrid;

namespace Client.Api;

internal partial class CoordinatorGateway
{
    public async Task<Dictionary<int, PeerModel>> GetAllHybridSessionPeers(int sessionId)
    {
        var request = new HybridSessionPeerListRequest(sessionId);
        var response = await SendRequestAsync<HybridSessionPeerListResponse>(request);

        return response.Peers.ToDictionary(pair => pair.Key, pair => pair.Value.ToModel());
    }

    public async Task<TransportParameters> JoinHybridSession(int sessionId, string clientEpoch)
    {
        var request = new HybridSessionJoinRequest(sessionId) { ClientEpoch = clientEpoch };
        var response = await SendRequestAsync<HybridSessionJoinResponse>(request);

        return response.TransportData?.ToModel()
               ?? throw new UnexpectedResponseException("The join of a hybrid session carries no transport");
    }

    public Task ConnectHybridSessionTransport(int sessionId, DtlsParameters dtlsParameters) =>
        SendRequestAsync<HybridSessionConnectTransportResponse>(
            new HybridSessionConnectTransportRequest(sessionId) { DTLSParameters = dtlsParameters.ToDto() });

    public async Task<OutboundTrack> StartVideoShareInHybridSession(int sessionId) =>
        (await SendRequestAsync<HybridSessionStartVideoResponse>(new HybridSessionStartVideoRequest(sessionId)))
        .Outbound.ToModel();

    public Task StopVideoShareInHybridSession(int sessionId) =>
        SendRequestAsync<HybridSessionStopVideoResponse>(new HybridSessionStopVideoRequest(sessionId));

    public async Task<OutboundTrack> StartVoiceShareInHybridSession(int sessionId) =>
        (await SendRequestAsync<HybridSessionStartVoiceResponse>(new HybridSessionStartVoiceRequest(sessionId)))
        .Outbound.ToModel();

    public Task StopVoiceShareInHybridSession(int sessionId) =>
        SendRequestAsync<HybridSessionStopVoiceResponse>(new HybridSessionStopVoiceRequest(sessionId));

    public async Task<ScreenShareOutbounds> StartScreenShareInHybridSession(int sessionId, bool withAudio)
    {
        var request = new HybridSessionStartScreenShareRequest(sessionId, withAudio);
        var response = await SendRequestAsync<HybridSessionStartScreenShareResponse>(request);

        return new ScreenShareOutbounds(response.Outbound.ToModel(), response.AudioOutbound?.ToModel());
    }

    public Task StopScreenShareInHybridSession(int sessionId) =>
        SendRequestAsync<HybridSessionStopScreenShareResponse>(new HybridSessionStopScreenShareRequest(sessionId));

    public async Task<ScreenShareInbounds?> SetScreenShareWatchInHybridSession(int sessionId, int userId,
        bool isWatching)
    {
        var request = new HybridSessionSetScreenShareWatchRequest(sessionId, userId, isWatching);
        var response = await SendRequestAsync<HybridSessionSetScreenShareWatchResponse>(request);

        return response.Inbound is { } inbound
            ? new ScreenShareInbounds(inbound.ToModel(), response.AudioInbound?.ToModel())
            : null;
    }

    public Task ResumeInboundsInHybridSession(int sessionId, IReadOnlyList<string> inboundIds) =>
        SendRequestAsync<HybridSessionResumeInboundsResponse>(
            new HybridSessionResumeInboundsRequest(sessionId, inboundIds.ToList()));

    public Task SetMicrophoneMuteInHybridSession(int sessionId, bool isMuted) =>
        SendRequestAsync<HybridSessionSetMicrophoneMuteResponse>(
            new HybridSessionSetMicrophoneMuteRequest(sessionId, isMuted));

    public Task SetSoundMuteInHybridSession(int sessionId, bool isMuted) =>
        SendRequestAsync<HybridSessionSetSoundMuteResponse>(new HybridSessionSetSoundMuteRequest(sessionId, isMuted));

    public Task SetAfkInHybridSession(int sessionId, bool isAfk, string? reason) =>
        SendRequestAsync<HybridSessionSetAfkResponse>(
            new HybridSessionSetAfkRequest(sessionId, isAfk, isAfk ? reason : null));

    public Task PokeInHybridSession(int sessionId, int userId, string? message) =>
        SendRequestAsync<HybridSessionPokeResponse>(new HybridSessionPokeRequest(sessionId, userId, message));
}
