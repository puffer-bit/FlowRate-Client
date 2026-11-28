namespace Client.Mobile.Platform;

/// <summary>
/// What the mobile views ask of the phone they run on. The host of the platform implements it and sets
/// <see cref="MobilePlatform.Current"/> before the first view is shown.
/// </summary>
public interface IMobilePlatform
{
    /// <summary>The version of the installed application, as the about page shows it.</summary>
    string AppVersion { get; }

    /// <summary>The operating system and its version, as the about page shows it.</summary>
    string SystemDescription { get; }

    /// <summary>Where the sound of a call goes.</summary>
    ICallAudioRoutes CallAudio { get; }

    /// <summary>Leaves the application.</summary>
    /// <param name="removeFromRecents">Whether it also leaves the list of recently used applications.</param>
    void Exit(bool removeFromRecents);

    /// <summary>Closes the text menu of the system, for a text box that has one open.</summary>
    /// <returns>Whether a menu was open.</returns>
    bool DismissTextMenu();

    /// <summary>Shows the text of a message where the system lets the user select and copy any part of it.</summary>
    void ShowSelectableText(string text);

    /// <summary>A short tick felt in the hand, for a gesture that has reached the point where it acts.</summary>
    void HapticTick();

    /// <summary>Turns the screen to landscape, or lets it follow the phone again.</summary>
    void SetLandscape(bool landscape);

    /// <summary>Offers the text to the other applications of the phone, by the share sheet of the system.</summary>
    void ShareText(string text, string title);

    /// <summary>Reads a QR code with the camera, by the scanner of the system.</summary>
    /// <returns>What the code holds; null when the user gave up.</returns>
    /// <exception cref="QrScanUnavailableException">The phone has no scanner to offer.</exception>
    System.Threading.Tasks.Task<string?> ScanQrCodeAsync();
}

/// <summary>The scanner of the system cannot be used: no camera, or the services it comes with are missing.</summary>
public sealed class QrScanUnavailableException(string message, System.Exception? inner = null)
    : System.Exception(message, inner);

/// <summary>The platform of the mobile views.</summary>
public static class MobilePlatform
{
    public static IMobilePlatform? Current { get; set; }
}
