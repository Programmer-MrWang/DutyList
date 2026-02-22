using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DutyListPlugin.Models;

namespace DutyListPlugin.Migration;

// ── 旧版 JSON 反序列化容器 ────────────────────────────────────────────────
// 旧版 duty.json 顶层结构：
//   {
//     "WeekConfig": {
//       "Monday": [ { "Start":"08:00:00", "End":"12:00:00", "Items":[...] } ],
//       ...
//     }
//   }

internal sealed class LegacyDutyConfig
{
    [JsonPropertyName("WeekConfig")]
    public Dictionary<string, List<LegacyTimeSlot>>? WeekConfig { get; set; }
}

internal sealed class LegacyTimeSlot
{
    [JsonPropertyName("Start")]
    public string Start { get; set; } = "08:00:00";

    [JsonPropertyName("End")]
    public string End { get; set; } = "12:00:00";

    [JsonPropertyName("Items")]
    public List<LegacyDutyItem> Items { get; set; } = new();
}

internal sealed class LegacyDutyItem
{
    [JsonPropertyName("Project")]
    public string Project { get; set; } = "";

    [JsonPropertyName("Person1")]
    public string Person1 { get; set; } = "";

    [JsonPropertyName("Person2")]
    public string Person2 { get; set; } = "";

    [JsonPropertyName("Person3")]
    public string Person3 { get; set; } = "";

    [JsonPropertyName("Color")]
    public string Color { get; set; } = "#00BFFF";
}

// ── 迁移器（公开 API）────────────────────────────────────────────────────

/// <summary>
/// 将旧版（DutyListPlugin v1）的 duty.json 配置迁移为新版 <see cref="DutyConfig"/>。
/// <para>
/// 旧版只有一套 WeekConfig，迁移后会被包装为名为"第1批"的 <see cref="RotationGroup"/>
/// 并添加到新版 <see cref="DutyConfig.Groups"/> 中，其余新版字段保留默认值。
/// </para>
/// </summary>
public static class LegacyConfigMigrator
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 判断指定路径的 JSON 文件是否为旧版配置格式。
    /// <para>判断依据：根节点含 <c>WeekConfig</c> 字段，且不含新版的 <c>Groups</c> 字段。</para>
    /// </summary>
    /// <param name="configPath">duty.json 的完整路径</param>
    public static bool IsLegacyConfig(string configPath)
    {
        if (!File.Exists(configPath)) return false;

        try
        {
            var text = File.ReadAllText(configPath);
            if (string.IsNullOrWhiteSpace(text)) return false;

            using var doc  = JsonDocument.Parse(text);
            var root       = doc.RootElement;
            bool hasOld    = root.TryGetProperty("WeekConfig", out _);
            bool hasNew    = root.TryGetProperty("Groups",     out _);
            return hasOld && !hasNew;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 读取旧版 duty.json 并转换为新版 <see cref="DutyConfig"/>。
    /// </summary>
    /// <param name="configPath">duty.json 的完整路径</param>
    /// <returns>
    /// 转换成功返回已填充数据的 <see cref="DutyConfig"/>；
    /// 文件不存在、格式不符或解析失败则返回 <c>null</c>。
    /// </returns>
    public static DutyConfig? Migrate(string configPath)
    {
        if (!File.Exists(configPath)) return null;

        try
        {
            var json   = File.ReadAllText(configPath);
            var legacy = JsonSerializer.Deserialize<LegacyDutyConfig>(json, _jsonOpts);

            if (legacy?.WeekConfig == null || legacy.WeekConfig.Count == 0)
                return null;

            // 旧版全部数据 → 单个 RotationGroup
            var group = new RotationGroup { Name = "第1批" };

            foreach (var (dayStr, legacySlots) in legacy.WeekConfig)
            {
                // WeekDay 枚举名与旧版字符串完全一致（Monday / Tuesday / …）
                if (!Enum.TryParse<WeekDay>(dayStr, ignoreCase: true, out var weekDay))
                    continue;

                var slotCol = new ObservableCollection<DutyTimeSlot>();

                foreach (var ls in legacySlots)
                {
                    var slot = new DutyTimeSlot
                    {
                        Start = TimeSpan.TryParse(ls.Start, out var st) ? st : TimeSpan.FromHours(8),
                        End   = TimeSpan.TryParse(ls.End,   out var et) ? et : TimeSpan.FromHours(12),
                    };

                    foreach (var li in ls.Items)
                    {
                        slot.Items.Add(new DutyItem
                        {
                            Project = li.Project,
                            Person1 = li.Person1,
                            Person2 = li.Person2,
                            Person3 = li.Person3,
                            Color   = li.Color,
                        });
                    }

                    slotCol.Add(slot);
                }

                group.WeekConfig[weekDay] = slotCol;
            }

            var newConfig = new DutyConfig();
            newConfig.Groups.Add(group);
            return newConfig;
        }
        catch
        {
            // 任何解析异常都视为无法迁移
            return null;
        }
    }
}
