namespace Client.Avalonia.Controls.Chat;

/// <summary>
/// An item of a message list that says what it holds, for the trace of a chat being opened: the time
/// a row takes is logged with it, which tells what makes some rows costly.
/// </summary>
public interface IChatOpenTraceItem
{
    /// <summary>A short note of what the row shows, such as its text length and attachments.</summary>
    string TraceDescription { get; }
}
