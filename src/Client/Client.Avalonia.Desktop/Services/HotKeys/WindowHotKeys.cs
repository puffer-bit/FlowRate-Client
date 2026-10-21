using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Client.Application.Settings;

namespace Client.Services.HotKeys;

/// <summary>
/// The hot keys pressed in a window, for the actions <see cref="GlobalHotKeys"/> does not hear. The
/// key is not handled: it still types in a text box, and the action is toggled as well.
/// </summary>
public static class WindowHotKeys
{
    public static void Attach(Window window, IHotKeysService service)
    {
        var down = new HashSet<PhysicalKey>();

        window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            // The window repeats a held key as more presses
            if (e.PhysicalKey == PhysicalKey.None || HotKeyGesture.IsModifier(e.PhysicalKey) || !down.Add(e.PhysicalKey))
                return;

            var settings = service.Current;
            if (!settings.IsEnabled)
                return;

            var modifiers = e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta);
            foreach (var binding in settings.Bindings)
            {
                if (HotKeyGesture.TryParse(binding.Gesture, out var gesture)
                    && gesture.Key == e.PhysicalKey && gesture.Modifiers == modifiers
                    && !service.IsHeardGlobally(binding.Action))
                {
                    service.Raise(binding.Action);
                }
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        window.AddHandler(InputElement.KeyUpEvent, (_, e) => down.Remove(e.PhysicalKey),
            RoutingStrategies.Tunnel, handledEventsToo: true);

        // The release of a key held while the window lost the focus never comes
        window.Deactivated += (_, _) => down.Clear();
    }
}
