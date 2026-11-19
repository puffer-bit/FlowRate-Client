using Client.Droid.Platform.Media;
using Xunit;

namespace Client.Application.Tests;

public class CallAudioRoutePolicyTests
{
    private static readonly CallAudioDevice Speaker = Device("speaker", 1, CallAudioDeviceKind.Speaker);
    private static readonly CallAudioDevice Earpiece = Device("earpiece", 2, CallAudioDeviceKind.Earpiece);
    private static readonly CallAudioDevice Bluetooth = Device("bluetooth", 3, CallAudioDeviceKind.Bluetooth);
    private static readonly CallAudioDevice Wired = Device("wired", 4, CallAudioDeviceKind.Wired);
    private static readonly CallAudioDevice Usb = Device("usb", 5, CallAudioDeviceKind.Usb);

    [Fact]
    public void ConnectedBluetoothTakesPriorityOverPhoneInAutomaticMode() =>
        Assert.Equal(Bluetooth, CallAudioRoutePolicy.Choose([Speaker, Earpiece, Bluetooth], null));

    [Fact]
    public void ExplicitSpeakerChoiceIsNotOverriddenWhenBluetoothConnects() =>
        Assert.Equal(Speaker, CallAudioRoutePolicy.Choose([Speaker, Bluetooth], Speaker.Key));

    [Fact]
    public void ExplicitHeadsetChoiceIsNotOverriddenByAnotherHeadset() =>
        Assert.Equal(Usb, CallAudioRoutePolicy.Choose([Speaker, Bluetooth, Usb], Usb.Key));

    [Fact]
    public void UnpluggedHeadsetFallsBackToAnotherConnectedHeadset() =>
        Assert.Equal(Wired, CallAudioRoutePolicy.Choose([Speaker, Earpiece, Wired], Bluetooth.Key));

    [Fact]
    public void DisconnectingTheLastHeadsetFallsBackToTheSpeaker() =>
        Assert.Equal(Speaker, CallAudioRoutePolicy.Choose([Earpiece, Speaker], Bluetooth.Key));

    [Fact]
    public void UsbIsPreferredToThePhoneInAutomaticMode() =>
        Assert.Equal(Usb, CallAudioRoutePolicy.Choose([Speaker, Earpiece, Usb], null));

    [Fact]
    public void NoCommunicationDevicesIsHandled() =>
        Assert.Null(CallAudioRoutePolicy.Choose([], Bluetooth.Key));

    private static CallAudioDevice Device(string key, int id, CallAudioDeviceKind kind) => new(key, id, kind, key);
}
