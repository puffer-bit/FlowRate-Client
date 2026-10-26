using System.Collections.Concurrent;
using Client.Application.Ports;
using Client.Core.Settings;
using Client.LibWebRTC.Voice;
using Shared.Enums.WebRTC;
using Xunit;

namespace Client.Application.Tests;

public class VoicePeerWorkerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task NativeCallbackReturnsBeforeApplicationCallbackRuns()
    {
        var nativeReturned = Completion();
        var applicationCalled = Completion();
        var disposed = Completion();
        var listener = new TestListener
        {
            Negotiated = _ =>
            {
                // Models an application lock held by the caller that is inside a native call.
                Assert.True(nativeReturned.Task.IsCompleted);
                applicationCalled.TrySetResult();
            }
        };
        using var worker = new VoicePeerWorker(callbacks => new TestPeer
        {
            NegotiateAction = () =>
            {
                callbacks.OnNegotiated("answer");
                nativeReturned.TrySetResult();
            },
            DisposeAction = () => disposed.TrySetResult()
        }, listener);

        worker.Negotiate("offer", [], []);
        await applicationCalled.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        worker.Dispose();
        await disposed.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Empty(listener.Errors);
    }

    [Fact]
    public async Task CommandsAndCloseDoNotWaitForBlockedNativeCall()
    {
        var entered = Completion();
        var release = Completion();
        var disposed = Completion();
        var calls = new ConcurrentQueue<string>();
        using var worker = new VoicePeerWorker(_ => new TestPeer
        {
            MicrophoneAction = enabled =>
            {
                calls.Enqueue("microphone");
                entered.TrySetResult();
                Assert.True(release.Task.Wait(Timeout, TestContext.Current.CancellationToken));
            },
            VolumeAction = () => calls.Enqueue("volume"),
            DisposeAction = () =>
            {
                calls.Enqueue("dispose");
                disposed.TrySetResult();
            }
        }, new TestListener());

        try
        {
            worker.SetMicrophoneEnabled(true);
            await entered.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
            worker.SetVolume("0", 0);
            worker.Dispose();
            worker.Dispose();
            worker.SetMicrophoneEnabled(false);
            Assert.False(disposed.Task.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
        }

        await disposed.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(["microphone", "dispose"], calls.ToArray());
    }

    [Fact]
    public async Task CommandsStayOrderedAndNegotiationInputsAreCopied()
    {
        var releaseCreation = Completion();
        var done = Completion();
        var calls = new ConcurrentQueue<string>();
        string[] mids = ["0"];
        uint[] ssrcs = [123];
        using var worker = new VoicePeerWorker(async _ =>
        {
            await releaseCreation.Task;
            return new TestPeer
            {
                MicrophoneAction = enabled => calls.Enqueue(enabled ? "on" : "off"),
                NegotiateInputs = (actualMids, actualSsrcs) =>
                {
                    Assert.Equal("0", Assert.Single(actualMids));
                    Assert.Equal(123u, Assert.Single(actualSsrcs));
                    calls.Enqueue("negotiate");
                    done.TrySetResult();
                }
            };
        }, new TestListener());

        worker.SetMicrophoneEnabled(true);
        worker.SetMicrophoneEnabled(false);
        worker.Negotiate("offer", mids, ssrcs);
        mids[0] = "changed";
        ssrcs[0] = 999;
        releaseCreation.TrySetResult();
        await done.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(["on", "off", "negotiate"], calls.ToArray());
    }

    [Fact]
    public async Task FailureIsReportedAndPeerIsReleased()
    {
        var failed = Completion();
        var disposed = Completion();
        var listener = new TestListener
        {
            ConnectionChanged = state =>
            {
                Assert.Equal(WebRTCConnectionState.Failed, state);
                failed.TrySetResult();
            }
        };
        using var worker = new VoicePeerWorker(_ => new TestPeer
        {
            NegotiateAction = () => throw new InvalidOperationException("native failure"),
            DisposeAction = () => disposed.TrySetResult()
        }, listener);

        worker.Negotiate("offer", [], []);
        await failed.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        await disposed.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal("native failure", Assert.Single(listener.Errors));
    }

    [Fact]
    public async Task LateCallbacksAfterCloseAreIgnored()
    {
        var created = new TaskCompletionSource<IVoicePeerListener>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = Completion();
        var listener = new TestListener();
        using var worker = new VoicePeerWorker(callbacks =>
        {
            created.TrySetResult(callbacks);
            return new TestPeer { DisposeAction = () => disposed.TrySetResult() };
        }, listener);
        var callbacks = await created.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);

        worker.Dispose();
        await disposed.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        callbacks.OnFailed("late failure");
        callbacks.OnNegotiated("late answer");
        Assert.Empty(listener.Errors);
    }

    [Fact]
    public async Task ClosingDuringCreationReleasesPeerWithoutExecutingQueuedCommands()
    {
        var entered = Completion();
        var release = Completion();
        var disposed = Completion();
        var calls = new ConcurrentQueue<string>();
        using var worker = new VoicePeerWorker(async _ =>
        {
            entered.TrySetResult();
            await release.Task;
            return new TestPeer
            {
                MicrophoneAction = _ => calls.Enqueue("microphone"),
                DisposeAction = () => disposed.TrySetResult()
            };
        }, new TestListener());

        await entered.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        worker.SetMicrophoneEnabled(true);
        worker.Dispose();
        release.TrySetResult();
        await disposed.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task CreationFailureIsReportedWithoutWaitingForConnectionTimeout()
    {
        var failed = Completion();
        var listener = new TestListener { ConnectionChanged = _ => failed.TrySetResult() };
        using var worker = new VoicePeerWorker(
            _ => Task.FromException<IVoicePeer>(new InvalidOperationException("permission denied")), listener);

        await failed.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal("permission denied", Assert.Single(listener.Errors));
    }

    private static TaskCompletionSource Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class TestListener : IVoicePeerListener
    {
        public ConcurrentQueue<string> Errors { get; } = new();
        public Action<string>? Negotiated { get; init; }
        public Action<WebRTCConnectionState>? ConnectionChanged { get; init; }
        public void OnConnectionChanged(WebRTCConnectionState state) => ConnectionChanged?.Invoke(state);
        public void OnNegotiated(string localSdp) => Negotiated?.Invoke(localSdp);
        public void OnFailed(string error) => Errors.Enqueue(error);
        public void OnStats(VoiceStats stats) { }
    }

    private sealed class TestPeer : IVoicePeer
    {
        public Action? NegotiateAction { get; init; }
        public Action<IReadOnlyList<string>, IReadOnlyList<uint>>? NegotiateInputs { get; init; }
        public Action<bool>? MicrophoneAction { get; init; }
        public Action? VolumeAction { get; init; }
        public Action? DisposeAction { get; init; }
        public void Negotiate(string offer, IReadOnlyList<string> sendMids, IReadOnlyList<uint> sendSsrcs)
        {
            NegotiateAction?.Invoke();
            NegotiateInputs?.Invoke(sendMids, sendSsrcs);
        }
        public void SetMicrophoneEnabled(bool isEnabled) => MicrophoneAction?.Invoke(isEnabled);
        public bool SetMicrophone(AudioInput input, NoiseSuppressionLevel noiseSuppression, bool isEchoCancellationEnabled) => true;
        public void SetMicrophoneVolume(double volume) { }
        public void SetVoiceGate(VoiceGateOptions? voiceGate) { }
        public void SetVolume(string mid, double volume) => VolumeAction?.Invoke();
        public void RequestStats() { }
        public void Dispose() => DisposeAction?.Invoke();
    }
}
