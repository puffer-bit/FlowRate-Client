using System;
using System.Threading.Tasks;
using Android.Gms.Extensions;
using Client.Application.Ports;
using Firebase.Messaging;
using Shared.Enums;

namespace Client.Droid.Platform.Firebase;

/// <summary>
/// The Firebase Cloud Messaging token of this installation.
/// </summary>
internal sealed class FirebasePushTokenProvider : IPushTokenProvider
{
    // The messaging service is created by the system, apart from the container: it reports here
    private static event Action<string>? TokenReplaced;

    public PushPlatform Platform => PushPlatform.Android;

    public event Action<string>? TokenChanged
    {
        add => TokenReplaced += value;
        remove => TokenReplaced -= value;
    }

    public static void ReportNewToken(string token) => TokenReplaced?.Invoke(token);

    public async Task<string?> GetTokenAsync()
    {
        try
        {
            // Marked obsolete by the binding only; it is the current way to ask Firebase for the token
#pragma warning disable CS0618
            var token = await FirebaseMessaging.Instance.GetToken().AsAsync<Java.Lang.String>();
#pragma warning restore CS0618
            return token?.ToString();
        }
        catch (Exception)
        {
            // No Google services on the device, or no network yet: the next login asks again
            return null;
        }
    }
}
