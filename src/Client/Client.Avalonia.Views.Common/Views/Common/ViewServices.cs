using System;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Views.Shared.Common;

/// <summary>
/// The services of the application for views that markup creates, which have no constructor to be given
/// them. The host sets the provider once it is built.
/// </summary>
public static class ViewServices
{
    public static IServiceProvider? Provider { get; set; }

    /// <returns>The service, or null in the designer and before the host has set the provider.</returns>
    public static T? Get<T>() where T : class => Provider?.GetService<T>();
}
