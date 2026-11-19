namespace Client.Core.Chat;

/// <summary>
/// What a web page says about itself, as the server fetched it for a link of a message. A preview never
/// changes: its image is kept by <see cref="Id"/>.
/// </summary>
public sealed record ChatLinkPreview
{
    public required int Id { get; init; }
    public required string Url { get; init; }
    public string? SiteName { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public bool HasImage { get; init; }
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }

    /// <summary>The image goes under the text in full width; otherwise it is a small picture beside it.</summary>
    public bool IsLargeImage { get; init; }
}
