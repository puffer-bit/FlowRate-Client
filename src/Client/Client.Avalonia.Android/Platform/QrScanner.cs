using System.Threading.Tasks;
using Android.Runtime;
using Client.Mobile.Platform;
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using Xamarin.Google.MLKit.Vision.CodeScanner;

namespace Client.Droid.Platform;

/// <summary>
/// Reads QR codes by the code scanner of Google Play services: its own screen over the application, with the camera
/// it asks for itself, so the application needs no camera permission for it.
/// </summary>
internal static class QrScanner
{
    // Barcode.FORMAT_QR_CODE
    private const int QrCodeFormat = 256;

    public static Task<string?> ScanAsync()
    {
        if (MainActivity.Current is not { } activity)
            throw new QrScanUnavailableException("The application is not on screen.");

        var options = new GmsBarcodeScannerOptions.Builder()
            .SetBarcodeFormats(QrCodeFormat, [])
            .Build();
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = new ScanListener(completion);
        try
        {
            GmsBarcodeScanning.GetClient(activity, options).StartScan()
                .AddOnSuccessListener(listener)
                .AddOnFailureListener(listener)
                .AddOnCanceledListener(listener);
        }
        catch (Java.Lang.Exception exception)
        {
            throw new QrScanUnavailableException(exception.Message ?? exception.GetType().Name, exception);
        }

        return completion.Task;
    }

    private sealed class ScanListener(TaskCompletionSource<string?> completion) : Java.Lang.Object,
        Android.Gms.Tasks.IOnSuccessListener, Android.Gms.Tasks.IOnFailureListener, Android.Gms.Tasks.IOnCanceledListener
    {
        public void OnSuccess(Java.Lang.Object? result) =>
            completion.TrySetResult(result?.JavaCast<Barcode>()?.RawValue);

        // Without Play services, or while its scanner module is still being downloaded
        public void OnFailure(Java.Lang.Exception exception) =>
            completion.TrySetException(new QrScanUnavailableException(exception.Message ?? exception.GetType().Name,
                exception));

        public void OnCanceled() => completion.TrySetResult(null);
    }
}
