namespace Client.Application.Updates;

public sealed record PublishedRelease(string Tag, string Name, string Markdown, DateTimeOffset PublishedAt, Uri Url);
