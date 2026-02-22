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
/// 旧版的星期几配置将按 周一=第1天 … 周日=第7天 映射到 DayConfig。
/// </summary>
public static class LegacyConfigMigrator
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // 旧版星期几 → 新版批次内天数（1-based），周一对应第1天
    private static readonly Dictionary<WeekDay, int> WeekToDayIndex = new()
    {
        { WeekDay.Monday,    1 }, { WeekDay.Tuesday,  2 },
        { WeekDay.Wednesday, 3 }, { WeekDay.Thursday, 4 },
        { WeekDay.Friday,    5 }, { WeekDay.Saturday, 6 },
        { WeekDay.Sunday,    7 }
    };

    /// <summary>
    /// 判断指定路径的 JSON 文件是否为旧版配置格式。
    /// 判断依据：根节点含 WeekConfig 字段，且不含新版的 Groups 字段。
    /// </summary>
    public static bool IsLegacyConfig(string configPath)
    {
        if (!File.Exists(configPath)) return false;
        try
        {
            var text = File.ReadAllText(configPath);
            if (string.IsNullOrWhiteSpace(text)) return false;
            using var doc = JsonDocument.Parse(text);
            var root      = doc.RootElement;
            return root.TryGetProperty("WeekConfig", out _) &&
                  !root.TryGetProperty("Groups",     out _);
        }
        catch { return false; }
    }

    /// <summary>
    /// 读取旧版 duty.json 并转换为新版 <see cref="DutyConfig"/>。
    /// 返回 null 表示文件不存在、格式不符或解析失败。
    /// </summary>
    public static DutyConfig? Migrate(string configPath)
    {
        if (!File.Exists(configPath)) return null;
        try
        {
            var json   = File.ReadAllText(configPath);
            var legacy = JsonSerializer.Deserialize<LegacyDutyConfig>(json, _jsonOpts);

            if (legacy?.WeekConfig == null || legacy.WeekConfig.Count == 0)
                return null;

            // 旧版全部数据 → 单个 RotationGroup，直接写入 DayConfig
            var group = new RotationGroup { Name = "第1批" };

            foreach (var (dayStr, legacySlots) in legacy.WeekConfig)
            {
                if (!Enum.TryParse<WeekDay>(dayStr, ignoreCase: true, out var weekDay)) continue;
                if (!WeekToDayIndex.TryGetValue(weekDay, out var dayIndex)) continue;

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

                group.DayConfig[dayIndex] = slotCol;   // 直接写 DayConfig，不再经过 WeekConfig
            }

            var newConfig = new DutyConfig();
            newConfig.Groups.Add(group);
            return newConfig;
        }
        catch { return null; }
    }
}
