using Client.Avalonia.Localization;
using System;
using Client.Core.Coordinator;
using Client.ViewModels.Dialogs;

namespace Client.ViewModels.Settings.Pages;

/// <summary>
/// A trusted certificate as shown in the settings.
/// </summary>
public class TrustedCertificateItem
{
    public ServerCertificateModel Certificate { get; }

    public string Address => $"{Certificate.Host}:{Certificate.Port}";
    public string Fingerprint => CertificateTrustMessages.Format(Certificate.Fingerprint);
    public string Subject => Certificate.Subject;
    public string ValidUntil => Strings.FormatDate(Certificate.NotAfter.ToLocalTime());
    public bool IsExpired => Certificate.NotAfter < DateTime.UtcNow;
    public string Coordinator { get; }

    /// <param name="coordinatorName">Name of the saved server with the bound coordinator, if there is one.</param>
    public TrustedCertificateItem(ServerCertificateModel certificate, string? coordinatorName)
    {
        Certificate = certificate;
        Coordinator = certificate.CoordinatorId switch
        {
            null => Strings.Get("settings.accounting.certificate.notBound"),
            _ when coordinatorName != null => $"{coordinatorName} ({certificate.CoordinatorId})",
            var id => id
        };
    }
}
