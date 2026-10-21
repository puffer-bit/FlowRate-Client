namespace Client.Core.Media;

/// <param name="ProcessId">The process that plays the sound of a window, or 0 for a monitor.</param>
public record ScreenSource(string Title, IntPtr Handle, uint ProcessId = 0);
