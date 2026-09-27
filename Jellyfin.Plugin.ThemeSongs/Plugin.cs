using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.ThemeSongs;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed class Plugin : BasePlugin<Settings>, IHasWebPages
{
    public static Plugin Instance { get; private set; } = null!;
    public override Guid Id => Guid.Parse("129e8a8b-87f1-48d3-802b-7dd151d72920");
    public override string Name => "JellyScore";
    public override string Description => "Automatically finds theme music for your movies and shows.";

    public Plugin(IApplicationPaths paths, IXmlSerializer serializer) : base(paths, serializer) => Instance = this;

    public IEnumerable<PluginPageInfo> GetPages() => [new()
    {
        Name = Name,
        EmbeddedResourcePath = "Jellyfin.Plugin.ThemeSongs.config.html",
        EnableInMainMenu = true,
        MenuIcon = "music_note"
    }];
}

// ReSharper disable once ClassNeverInstantiated.Global
public sealed class Settings : BasePluginConfiguration
{
    public bool Enabled { get; set; } = true;
    public Guid[]? Libraries { get; set; }

    public Guid[] SelectedLibraries(ILibraryManager library) => Libraries ?? library.GetVirtualFolders().Select(f => Guid.Parse(f.ItemId)).ToArray();
}

// ReSharper disable once UnusedType.Global
public sealed class Registration : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, MediaBrowser.Controller.IServerApplicationHost host)
    {
        services.AddSingleton<YouTube>();
        services.AddSingleton<Store>();
        services.AddSingleton<ThemeService>();
        services.AddHostedService<NewItemWorker>();
        services.AddHostedService<LibraryScanWorker>();
        services.AddSingleton<ThemeScan>();
        services.AddSingleton<MediaBrowser.Model.Tasks.IScheduledTask>(sp => sp.GetRequiredService<ThemeScan>());
    }
}
