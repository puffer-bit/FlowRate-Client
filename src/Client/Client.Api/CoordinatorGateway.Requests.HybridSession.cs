using Client.Api.Mappers;
using Shared.Enums;
using Shared.Models.Requests.SessionActions.HybridSession;
using Shared.Models.Responses.SessionActions.HybridSession;
using Client.Core.Sessions.Hybrid;

namespace Client.Api;

internal partial class CoordinatorGateway
{
    public async Task<(PeerListRequestResult Result, Dictionary<int, PeerModel>? Peers)> GetAllHybridSessionPeers(
        int sessionId)
    {
        var request = new HybridSessionPeerListRequest(sessionId);
        var infoResponse = await SendRequestAsync<HybridSessionPeerListResponse>(request);
        
        return (infoResponse.Result, infoResponse.Peers?
            .ToDictionary(pair => pair.Key, pair => pair.Value.ToModel()));
    }
    
    public async Task<(HybridSessionJoinResult Result, TransportParameters? Transport)> JoinHybridSession(
        int sessionId, 
        string clientEpoch)
    {
        var request = new HybridSessionJoinRequest(sessionId) { ClientEpoch = clientEpoch };
        var joinSessionResponse = await SendRequestAsync<HybridSessionJoinResponse>(request);
        
        return (joinSessionResponse.Result, joinSessionResponse.TransportData?.ToModel());
    }

    public async Task<HybridSessionConnectTransportResult> ConnectHybridSessionTransport(
        int sessionId,
        DtlsParameters dtlsParameters)
    {
        var request = new HybridSessionConnectTransportRequest(sessionId) { DtlsParameters = dtlsParameters.ToDto() };
        var connectResponse = await SendRequestAsync<HybridSessionConnectTransportResponse>(request);

        return connectResponse.Result;
    }

    public async Task<(StartVideoShareResult Result, OutboundTrack? Outbound)> StartVideoShareInHybridSession(int sessionId)
    {
        var request = new HybridSessionStartVideoRequest(sessionId);
        var startVideoResponse = await SendRequestAsync<HybridSessionStartVideoResponse>(request);
        
        return (startVideoResponse.Result, startVideoResponse.Outbound?.ToModel());
    }

    public async Task<StopVideoShareResult> StopVideoShareInHybridSession(int sessionId)
    {
        var request = new HybridSessionStopVideoRequest(sessionId);
        var stopVideoResponse = await SendRequestAsync<HybridSessionStopVideoResponse>(request);
        
        return stopVideoResponse.Result;
    }
    
    public async Task<(StartVoiceShareResult Result, OutboundTrack? Outbound)> StartVoiceShareInHybridSession(int sessionId)
    {
        var request = new HybridSessionStartVoiceRequest(sessionId);
        var startVoiceResponse = await SendRequestAsync<HybridSessionStartVoiceResponse>(request);
        
        return (startVoiceResponse.Result, startVoiceResponse.Outbound?.ToModel());
    }

    public async Task<StopVoiceShareResult> StopVoiceShareInHybridSession(int sessionId)
    {
        var request = new HybridSessionStopVoiceRequest(sessionId);
        var stopVideoResponse = await SendRequestAsync<HybridSessionStopVoiceResponse>(request);
        
        return stopVideoResponse.Result;
    }

    public async Task<(StartScreenShareResult Result, ScreenShareOutbounds? Outbounds)> StartScreenShareInHybridSession(
        int sessionId, bool withAudio)
    {
        var request = new HybridSessionStartScreenShareRequest(sessionId, withAudio);
        var response = await SendRequestAsync<HybridSessionStartScreenShareResponse>(request);

        return (response.Result, response.Outbound is { } outbound
            ? new ScreenShareOutbounds(outbound.ToModel(), response.AudioOutbound?.ToModel())
            : null);
    }

    public async Task<StopScreenShareResult> StopScreenShareInHybridSession(int sessionId)
    {
        var request = new HybridSessionStopScreenShareRequest(sessionId);
        var response = await SendRequestAsync<HybridSessionStopScreenShareResponse>(request);

        return response.Result;
    }

    public async Task<(SetScreenShareWatchResult Result, ScreenShareInbounds? Inbounds)> SetScreenShareWatchInHybridSession(
        int sessionId, int userId, bool isWatching)
    {
        var request = new HybridSessionSetScreenShareWatchRequest(sessionId, userId, isWatching);
        var response = await SendRequestAsync<HybridSessionSetScreenShareWatchResponse>(request);

        return (response.Result, response.Inbound is { } inbound
            ? new ScreenShareInbounds(inbound.ToModel(), response.AudioInbound?.ToModel())
            : null);
    }

    public async Task<ResumeInboundsResult> ResumeInboundsInHybridSession(int sessionId,
        IReadOnlyList<string> inboundIds)
    {
        var request = new HybridSessionResumeInboundsRequest(sessionId, inboundIds.ToList());
        var response = await SendRequestAsync<HybridSessionResumeInboundsResponse>(request);

        return response.Result;
    }

    public async Task<SetMicrophoneMuteResult> SetMicrophoneMuteInHybridSession(int sessionId, bool isMuted)
    {
        var request = new HybridSessionSetMicrophoneMuteRequest(sessionId, isMuted);
        var muteResponse = await SendRequestAsync<HybridSessionSetMicrophoneMuteResponse>(request);
        
        return muteResponse.Result;
    }

    public async Task<SetSoundMuteResult> SetSoundMuteInHybridSession(int sessionId, bool isMuted)
    {
        var request = new HybridSessionSetSoundMuteRequest(sessionId, isMuted);
        var muteResponse = await SendRequestAsync<HybridSessionSetSoundMuteResponse>(request);

        return muteResponse.Result;
    }

    public async Task<SetAfkResult> SetAfkInHybridSession(int sessionId, bool isAfk, string? reason)
    {
        var request = new HybridSessionSetAfkRequest(sessionId, isAfk, isAfk ? reason : null);
        var response = await SendRequestAsync<HybridSessionSetAfkResponse>(request);

        return response.Result;
    }

    public async Task<PokeResult> PokeInHybridSession(int sessionId, int userId, string? message)
    {
        var request = new HybridSessionPokeRequest(sessionId, userId, message);
        var response = await SendRequestAsync<HybridSessionPokeResponse>(request);

        return response.Result;
    }
}