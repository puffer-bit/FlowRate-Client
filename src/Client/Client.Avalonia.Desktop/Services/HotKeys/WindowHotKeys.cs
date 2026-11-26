using System;
using System.Collections.Generic;
using System.Linq;
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
        // The actions that act while held, by the key whose release ends them
        var held = new Dictionary<Client.Core.Settings.HotKeyAction, PhysicalKey>();

        void Release(Func<PhysicalKey, bool> released)
        {
            foreach (var (action, key) in held.ToList())
            {
                if (!released(key))
                    continue;

                held.Remove(action);
                service.RaiseRelease(action);
            }
        }

        window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            // The window repeats a held key as more presses
            if (e.PhysicalKey == PhysicalKey.None || !down.Add(e.PhysicalKey))
                return;

            var settings = service.Current;
            if (!settings.IsEnabled)
                return;

            var pressed = HotKeyGesture.Of(e.KeyModifiers, e.PhysicalKey);
            foreach (var binding in settings.Bindings)
            {
                if (HotKeyGesture.TryParse(binding.Gesture, out var gesture)
                    && gesture == pressed
                    && !service.IsHeardGlobally(binding.Action))
                {
                    if (Client.Core.Settings.HotKeysSettings.IsHeld(binding.Action))
                        held[binding.Action] = e.PhysicalKey;
                    service.Raise(binding.Action);
                }
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        window.AddHandler(InputElement.KeyUpEvent, (_, e) =>
        {
            down.Remove(e.PhysicalKey);
            Release(key => key == e.PhysicalKey);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        // The release of a key held while the window lost the focus never comes
        window.Deactivated += (_, _) =>
        {
            down.Clear();
            Release(_ => true);
        };
    }
}
