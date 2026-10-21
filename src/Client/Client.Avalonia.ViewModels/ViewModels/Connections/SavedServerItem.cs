namespace Client.ViewModels.Connections;

/// <summary>
/// A remembered server offered in the address field of the connect window.
/// </summary>
public sealed record SavedServerItem(string? Name, string Host, int Port)
{
    public string Endpoint => $"{Host}:{Port}";

    // The editable ComboBox puts this into its text when the item is picked; the port has its own field
    public override string ToString() => Host;
}
