using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace DutyListPlugin.Models;

/// <summary>
/// 一个轮换批次，按"批次内第几天"（1-based）存储配置。
/// SkipDays 列表内的星期几将被跳过，天数仅计其余日期。
/// </summary>
public class RotationGroup : INotifyPropertyChanged
{
    private string _name           = "批次";
    private bool   _enableReminder = true;
    private List<DayOfWeek> _skipDays = new();

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    /// <summary>本批次是否启用提醒（独立于全局开关，两者均需为 true 才发送提醒）。</summary>
    public bool EnableReminder
    {
        get => _enableReminder;
        set { _enableReminder = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 计算批次内天数时需要跳过的星期几列表。
    /// 空列表 = 不跳过任何天。
    /// </summary>
    public List<DayOfWeek> SkipDays
    {
        get => _skipDays;
        set { _skipDays = value ?? new(); OnPropertyChanged(); }
    }

    /// <summary>
    /// 按批次内天数（1-based）存储的配置。
    /// </summary>
    public Dictionary<int, ObservableCollection<DutyTimeSlot>> DayConfig { get; set; } = new();

    // ── 向后兼容：旧版 SkipSunday → SkipDays ──────────────────────────
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SkipSunday
    {
        get => false;
        set { if (value && !_skipDays.Contains(DayOfWeek.Sunday)) _skipDays.Add(DayOfWeek.Sunday); }
    }

    // ── 向后兼容：旧版 WeekConfig → DayConfig ─────────────────────────
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<WeekDay, ObservableCollection<DutyTimeSlot>>? WeekConfig
    {
        get => null;
        set
        {
            if (value == null || value.Count == 0 || DayConfig.Count > 0) return;
            var map = new Dictionary<WeekDay, int>
            {
                { WeekDay.Monday,    1 }, { WeekDay.Tuesday,  2 },
                { WeekDay.Wednesday, 3 }, { WeekDay.Thursday, 4 },
                { WeekDay.Friday,    5 }, { WeekDay.Saturday, 6 },
                { WeekDay.Sunday,    7 }
            };
            foreach (var (day, slots) in value)
                if (map.TryGetValue(day, out var idx)) DayConfig[idx] = slots;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
