using System.Collections.Generic;

namespace Client.Services;

/// <summary>
/// The DrawingImage resource of each icon type, by its key in Client.Avalonia.Assets/IconImages.axaml
/// </summary>
public static class IconKeys
{
    private static readonly Dictionary<IconType, string> Keys = new()
    {
        // Status icons
        { IconType.Warning, "WarningIcon" },
        { IconType.KeyWarning, "KeyWarningIcon" },
        { IconType.KeyQuestion, "KeyQuestionIcon" },
        { IconType.Error, "CrossInCircleIcon" },
        { IconType.Info, "IInCircleIcon" },
        { IconType.Success, "MarkInCircleIcon" },
        { IconType.Question, "QuestionInCurcleIcon" },
        { IconType.Stop, "BrickInCircleIcon" },
        
        // User related
        { IconType.BaseUser, "BaseUserIcon" },
        { IconType.BaseUsers, "BaseUsersIcon" },
        
        // Session related
        { IconType.VideoSession, "HybridSessionIcon" },
        { IconType.ConnectToInstance, "KeyIcon" },
        
        // Action icons
        { IconType.Save, "DisketteIcon" },
        { IconType.Delete, "ExplodeIcon" },
        { IconType.New, "PlusIcon" },
        { IconType.Find, "FindIcon" },
        { IconType.Reset, "ResetIcon" },
        
        // Security
        { IconType.Shield, "BlueShieldIcon" },
        { IconType.GoodShield, "GreenShieldIcon" },
        { IconType.BadShield, "RedShieldIcon" },
        { IconType.StrangeShield, "OrangeShieldIcon" },
        
        // Media
        { IconType.Microphone, "MicrophoneIcon" },
        { IconType.MicrophoneMuted, "CrossedMicrophoneIcon" },
        { IconType.MaxVolume, "HighVolumeIcon" },
        
        // UI Controls
        { IconType.ShowPassword, "EyeIcon" },
        { IconType.HidePassword, "CrossedEyeIcon" },
        { IconType.CloseNotification, "CloseIcon" },
        { IconType.GoFullscreen, "FullscreenIcon" },
        { IconType.GoLive, "PlayIcon" },
        
        // States
        { IconType.Completed, "MarkInCircleIcon" },
        { IconType.Failed, "DashInCircleIcon" },
        { IconType.Joined, "DoorWithMarkIcon" },
        { IconType.NotJoined, "DoorWithCrossIcon" },
        
        // Server/Connection
        { IconType.Server, "ServerIcon" },
        { IconType.Host, "CrownIcon" },
        
        // Settings
        { IconType.Settings, "GearIcon" },
        
        // Actions
        { IconType.Logout, "DoorWithRedArrowIcon" },
        { IconType.Login, "DoorWithGreenArrowIcon" },
        { IconType.KickFromSession, "DoorWithCrossIcon" },
        { IconType.Crush, "ExplodeIcon" },
        
        // Other
        { IconType.Yes, "MarkIcon" },
        { IconType.No, "CrossIcon" },
        { IconType.Tip, "TipIcon" },
        { IconType.Stats, "StatsIcon" }
    };

    public static string Of(IconType iconType)
    {
        if (!Keys.TryGetValue(iconType, out var iconKey))
        {
            throw new KeyNotFoundException($"Icon type '{iconType}' is not mapped to any icon resource.");
        }

        return iconKey;
    }
}
