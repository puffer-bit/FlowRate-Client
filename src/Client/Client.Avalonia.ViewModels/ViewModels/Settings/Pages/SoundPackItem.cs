namespace Client.ViewModels.Settings.Pages;

public class SoundPackItem
{
    /// <summary>
    /// Null for the built-in sounds.
    /// </summary>
    public string? Name { get; }
    public string Title { get; }

    public SoundPackItem(string? name, string title)
    {
        Name = name;
        Title = title;
    }
}
