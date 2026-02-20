using System.IO;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Shared.Helpers;
using DutyListPlugin.Components;
using DutyListPlugin.Models;
using DutyListPlugin.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DutyListPlugin;

[PluginEntrance]
public class Plugin : PluginBase
{
    public static DutyConfig Config { get; private set; } = new();
    private static string _configPath = "";

    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        _configPath = Path.Combine(PluginConfigFolder, "duty.json");
        Config = ConfigureFileHelper.LoadConfig<DutyConfig>(_configPath) ?? new DutyConfig();
        Config.PropertyChanged += (_, _) => Save();

        services.AddSettingsPage<DutySettingsPage>();
        services.AddComponent<DutyDisplayComponent>();
    }

    public static void Save()
    {
        if (!string.IsNullOrEmpty(_configPath))
            ConfigureFileHelper.SaveConfig(_configPath, Config);
    }
}
