using Client.Avalonia.Localization;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Client.Application.Coordinator;
using ReactiveUI;

namespace Client.ViewModels.Dialogs;

/// <summary>
/// What the user is asked when a server presents a certificate the system cannot verify.
/// </summary>
public static class CertificateTrustMessages
{
    /// <summary>The question to trust the certificate, answered with Yes or No.</summary>
    public static MessageBoxVM CreatePrompt(UntrustedCertificate untrusted)
    {
        var certificate = untrusted.Certificate;
        var server = $"{certificate.Host}:{certificate.Port}";

        if (untrusted.IsChanged)
            return new MessageBoxVM(Icon.KeyWarning, Buttons.YesNo,
                Strings.Format("certificate.changed.message", ("server", server), ("trusted", Format(untrusted.KnownFingerprint!)),
                    ("presented", Format(certificate.Fingerprint))),
                Strings.Get("certificate.changed.title"));

        return new MessageBoxVM(Icon.KeyQuestion, Buttons.YesNo,
            Strings.Format("certificate.unknown.message", ("server", server), ("subject", certificate.Subject),
                ("validUntil", Strings.FormatDate(certificate.NotAfter.ToLocalTime())), ("fingerprint", Format(certificate.Fingerprint))),
            Strings.Get("certificate.unknown.title"));
    }

    /// <summary>
    /// Asks whether to trust the certificate the server presented, and trusts it on a yes.
    /// </summary>
    /// <returns>True when the user trusted it.</returns>
    public static async Task<bool> ConfirmTrustAsync(ICoordinatorSession session, Interaction<MessageBoxVM, Result> showMessageBox)
    {
        if (session.UntrustedCertificate is not { } untrusted
            || await showMessageBox.Handle(CreatePrompt(untrusted)) != Result.Yes)
            return false;

        await session.TrustCertificateAsync(untrusted);
        return true;
    }

    /// <summary>The message shown when the server is not the coordinator saved under its address.</summary>
    public static MessageBoxVM CreateMismatchMessage(CoordinatorMismatch? mismatch) =>
        new(Icon.KeyWarning, Buttons.Ok, GetCoordinatorMismatch(mismatch), CoordinatorMismatchTitle);

    /// <summary>The message shown when a reconnect stopped because the certificate changed.</summary>
    public static string GetReconnectStopped(UntrustedCertificate? untrusted) =>
        untrusted is { IsChanged: true }
            ? Strings.Format("certificate.reconnect.changed", ("fingerprint", Format(untrusted.Certificate.Fingerprint)))
            : Strings.Get("certificate.reconnect.notTrusted");

    public static string CoordinatorMismatchTitle => Strings.Get("certificate.mismatch.title");

    /// <summary>The message shown when a trusted address led to another coordinator.</summary>
    public static string GetCoordinatorMismatch(CoordinatorMismatch? mismatch) =>
        mismatch == null
            ? Strings.Get("certificate.mismatch.short")
            : Strings.Format("certificate.mismatch.message", ("address", $"{mismatch.Host}:{mismatch.Port}"),
                ("expected", mismatch.ExpectedCoordinatorId),
                ("answered", string.IsNullOrWhiteSpace(mismatch.ActualCoordinatorName)
                    ? $"({mismatch.ActualCoordinatorId})"
                    : $"{mismatch.ActualCoordinatorName} ({mismatch.ActualCoordinatorId})"),
                ("tab", Strings.Get("settings.accounting.name")), ("section", Strings.Get("settings.accounting.section.trustedCertificates")));

    // Groups of four, two lines: easier to compare by eye than 64 characters in a row
    public static string Format(string fingerprint)
    {
        var groups = Enumerable.Range(0, fingerprint.Length / 4).Select(i => fingerprint.Substring(i * 4, 4)).ToList();
        return string.Join(" ", groups.Take(groups.Count / 2)) + "\n" + string.Join(" ", groups.Skip(groups.Count / 2));
    }
}
