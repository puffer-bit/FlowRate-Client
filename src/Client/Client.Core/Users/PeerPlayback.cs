namespace Client.Core.Users;

/// <summary>
/// How a user is played on this client. Local only, the other listeners are not affected.
/// </summary>
/// <param name="Volume">Slider position, up to <see cref="MaxVolume"/>. Up to 1 it is the linear gain.</param>
public readonly record struct PeerPlayback(double Volume, bool IsMuted, bool IsOverdriveEnabled)
{
    public const double NormalMaxVolume = 1.0;
    public const double OverdriveMaxVolume = 2.0;

    // Gain at the end of the overdrive range: +18 dB. A doubling is only +6 dB, which a quiet
    // speaker barely gains from, so above 1 the slider is exponential and every step adds the same dB.
    public const double OverdriveMaxGain = 8.0;

    public static PeerPlayback Default => new(1.0, false, false);

    public double MaxVolume => IsOverdriveEnabled ? OverdriveMaxVolume : NormalMaxVolume;

    /// <summary>
    /// The gain actually applied to the stream.
    /// </summary>
    public double EffectiveVolume
    {
        get
        {
            if (IsMuted)
                return 0;

            var position = Math.Clamp(Volume, 0, MaxVolume);

            return position <= NormalMaxVolume
                ? position
                : Math.Pow(OverdriveMaxGain, (position - NormalMaxVolume) / (OverdriveMaxVolume - NormalMaxVolume));
        }
    }
}
