using System;
using Avalonia.Input;
using Client.Core.Settings;

namespace Client.Services.HotKeys;

/// <summary>
/// Hears every key pressed on the machine without taking it from the application it is meant for.
/// </summary>
internal interface IGlobalKeyListener : IDisposable
{
    GlobalHotKeysBackend Kind { get; }
}

/// <summary>
/// A key went down or up. Called on the thread of the listener.
/// </summary>
/// <param name="isRepeat">The key is held and the keyboard repeats it.</param>
internal delegate void GlobalKeyHandler(PhysicalKey key, bool isDown, bool isRepeat);

/// <summary>
/// The state of a modifier as the system sees it, for a release the listener never got. Called on
/// the thread of the listener.
/// </summary>
internal delegate void GlobalModifierSync(PhysicalKey key, bool isDown);
