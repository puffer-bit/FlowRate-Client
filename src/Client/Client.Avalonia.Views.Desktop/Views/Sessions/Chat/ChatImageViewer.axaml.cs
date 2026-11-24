using Avalonia.Controls;
using Client.ViewModels.Sessions.Chat;

namespace Client.Views.Sessions.Chat;

/// <summary>Adapts attachment loading and commands to the common desktop picture viewer.</summary>
internal partial class ChatImageViewer : UserControl
{
    private ImagePreviewVM? _preview;

    public ChatImageViewer()
    {
        InitializeComponent();
        void UpdatePreview()
        {
            _preview?.Dispose();
            _preview = DataContext is ChatImageViewerVM attachment ? new ImagePreviewVM(attachment) : null;
            Content = _preview == null ? null : new ImagePreviewView { DataContext = _preview };
        }
        DataContextChanged += (_, _) => UpdatePreview();
        AttachedToVisualTree += (_, _) => { if (_preview == null) UpdatePreview(); };
        DetachedFromVisualTree += (_, _) => { _preview?.Dispose(); _preview = null; Content = null; };
    }
}
