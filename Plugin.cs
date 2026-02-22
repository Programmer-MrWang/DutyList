using System.IO;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Shared.Helpers;
using DutyListPlugin.Components;
using DutyListPlugin.Migration;
using DutyListPlugin.Models;
using DutyListPlugin.Notifications;
using DutyListPlugin.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DutyListPlugin;

[PluginEntrance]
public class Plugin : PluginBase
{
    public static DutyConfig Config { get; private set; } = new();

    /// <summary>设置页点击"立即刷新"时触发，通知所有 DutyDisplayComponent 立即重绘。</summary>
    public static event Action? DisplayRefreshRequested;
    private static string _configPath = "";

    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        _configPath = Path.Combine(PluginConfigFolder, "duty.json");

        // ── 加载配置 ──────────────────────────────────────────────────────
        // 若文件为旧版格式（含 WeekConfig 但不含 Groups），先自动迁移再保存。
        if (LegacyConfigMigrator.IsLegacyConfig(_configPath))
        {
            var migrated = LegacyConfigMigrator.Migrate(_configPath);
            if (migrated != null)
            {
                Config = migrated;
                // 立即将迁移结果写回磁盘，覆盖旧格式
                ConfigureFileHelper.SaveConfig(_configPath, Config);
            }
            else
            {
                Config = new DutyConfig();
            }
        }
        else
        {
            Config = ConfigureFileHelper.LoadConfig<DutyConfig>(_configPath) ?? new DutyConfig();
        }

        Config.PropertyChanged += (_, _) => Save();

        services.AddSettingsPage<DutySettingsPage>();
        services.AddComponent<DutyDisplayComponent>();

        // 注册提醒提供方 —— 在值日时段开始时发出 ClassIsland 提醒
        services.AddNotificationProvider<DutyNotificationProvider>();
    }

    public static void Save()
    {
        if (!string.IsNullOrEmpty(_configPath))
            ConfigureFileHelper.SaveConfig(_configPath, Config);
    }

    public static void TriggerDisplayRefresh() => DisplayRefreshRequested?.Invoke();
}
