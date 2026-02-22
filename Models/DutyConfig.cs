using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DutyListPlugin.Models;

public class DutyConfig : INotifyPropertyChanged
{
    // ── 批次列表 ─────────────────────────────────────────────────────────
    private ObservableCollection<RotationGroup> _groups = new();
    public ObservableCollection<RotationGroup> Groups
    {
        get => _groups;
        set
        {
            _groups.CollectionChanged -= OnAnyChanged;
            _groups = value;
            _groups.CollectionChanged += OnAnyChanged;
            OnPropertyChanged();
        }
    }

    // ── 轮换参数 ─────────────────────────────────────────────────────────
    private DateTime _rotationStartDate = DateTime.Today;
    public DateTime RotationStartDate
    {
        get => _rotationStartDate;
        set { _rotationStartDate = value.Date; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
    }

    private int _rotationPeriodDays = 7;
    public int RotationPeriodDays
    {
        get => _rotationPeriodDays;
        set { _rotationPeriodDays = Math.Max(1, value); OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
    }

    // ── 提醒设置（全局）─────────────────────────────────────────────────
    private bool _enableReminder = true;
    public bool EnableReminder
    {
        get => _enableReminder;
        set { _enableReminder = value; OnPropertyChanged(); }
    }

    private bool _enableReminderSound = true;
    public bool EnableReminderSound
    {
        get => _enableReminderSound;
        set { _enableReminderSound = value; OnPropertyChanged(); }
    }

    private string _ttsVoice = "";
    public string TtsVoice
    {
        get => _ttsVoice;
        set { _ttsVoice = value ?? ""; OnPropertyChanged(); }
    }

    // ── 当前批次 + 天数计算 ───────────────────────────────────────────────
    /// <summary>
    /// 返回当前批次及批次内天数（1-based）。
    /// dayIndex = 0 表示今天是跳过日（无值日）。
    /// </summary>
    public (RotationGroup? Group, int DayIndex) GetCurrentGroupAndDay()
    {
        if (Groups.Count == 0) return (null, 0);

        var today      = DateTime.Today;
        var daysPassed = Math.Max(0, (int)(today - RotationStartDate).TotalDays);
        var batchNo    = daysPassed / RotationPeriodDays;
        var idx        = batchNo % Groups.Count;
        var group      = Groups[idx];
        var batchStart = RotationStartDate.AddDays(batchNo * RotationPeriodDays);

        int dayIndex;
        if (group.SkipDays.Count > 0)
        {
            // 今天是跳过日 → 不显示值日
            if (group.SkipDays.Contains(today.DayOfWeek))
                return (group, 0);

            // 从批次起始日到今天（含），累计非跳过天数
            int counted = 0;
            for (var d = batchStart; d <= today; d = d.AddDays(1))
                if (!group.SkipDays.Contains(d.DayOfWeek))
                    counted++;
            dayIndex = Math.Max(1, counted);
        }
        else
        {
            dayIndex = (daysPassed % RotationPeriodDays) + 1;
        }

        return (group, dayIndex);
    }

    public RotationGroup? GetCurrentGroup() => GetCurrentGroupAndDay().Group;

    public string StatusText
    {
        get
        {
            var (group, dayIndex) = GetCurrentGroupAndDay();
            if (group == null) return "（尚未创建批次）";

            var daysPassed = Math.Max(0, (int)(DateTime.Today - RotationStartDate).TotalDays);
            var batchNo    = daysPassed / RotationPeriodDays;
            var batchStart = RotationStartDate.AddDays(batchNo * RotationPeriodDays);
            var batchEnd   = batchStart.AddDays(RotationPeriodDays - 1);
            var daysLeft   = (batchEnd - DateTime.Today).Days + 1;

            var skipNote = group.SkipDays.Count > 0
                ? $"（跳过{group.SkipDays.Count}种日期）"
                : "";

            var dayNote = dayIndex == 0 ? "今日休息" : $"第{dayIndex}天";
            return $"当前：{group.Name} {dayNote}{skipNote}  （{batchStart:M/d}–{batchEnd:M/d}，还剩 {daysLeft} 天）";
        }
    }

    public void SubscribeCollection(ObservableCollection<DutyTimeSlot> col)
    {
        col.CollectionChanged -= OnAnyChanged;
        col.CollectionChanged += OnAnyChanged;
    }

    private void OnAnyChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(Groups));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
