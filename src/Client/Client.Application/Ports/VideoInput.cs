using System.Runtime.InteropServices;

namespace Client.Application.Ports;

/// <summary>
/// Where an outbound video track takes its picture from. Kept free of media engine types, so that
/// code without a reference to the engine can choose the input.
/// </summary>
public abstract record VideoInput;

/// <summary>
/// A screen or window the user picked in the system dialog of the desktop portal.
/// </summary>
/// <param name="NodeId">PipeWire node the portal streams the picture to.</param>
/// <param name="Remote">
/// PipeWire connection the portal opened for this stream. The node is only visible through it,
/// and it has to stay open for as long as the stream is captured.
/// </param>
/// <param name="Width">Width of the picture in pixels as the portal reported it, or 0 when it did not.</param>
/// <param name="Height">Height of the picture in pixels, or 0 when the portal did not report it.</param>
public sealed record PipeWireVideoInput(uint NodeId, SafeHandle Remote, int Width = 0, int Height = 0) : VideoInput;

/// <summary>
/// A whole monitor, by its HMONITOR.
/// </summary>
public sealed record MonitorVideoInput(IntPtr Handle) : VideoInput;

/// <summary>
/// A single window, by its HWND.
/// </summary>
public sealed record WindowVideoInput(IntPtr Handle) : VideoInput;

/// <summary>
/// A moving test picture.
/// </summary>
public sealed record TestPatternVideoInput : VideoInput;
