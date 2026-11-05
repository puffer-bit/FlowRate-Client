using Avalonia.Controls;

namespace Client.Avalonia.Controls.Avatars;

/// <summary>A transparent avatar action with a pencil shown on hover or keyboard focus.</summary>
public partial class AvatarEditButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    public AvatarEditButton() => InitializeComponent();
}
