namespace Client.Services;

public interface ILanguageService
{
    /// <summary>
    /// Switches the interface to the language, open windows included.
    /// </summary>
    /// <param name="code">Language code such as "ru", or null for the language of the operating system.</param>
    void Apply(string? code);
}
