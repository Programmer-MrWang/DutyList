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

    /// <summary>第一批次生效的起始日期（当天0点）。</summary>
    private DateTime _rotationStartDate = DateTime.Today;
    public DateTime RotationStartDate
    {
        get => _rotationStartDate;
        set { _rotationStartDate = value.Date; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
    }

    /// <summary>每个批次持续的天数（整数，≥1）。</summary>
    private int _rotationPeriodDays = 7;
    public int RotationPeriodDays
    {
        get => _rotationPeriodDays;
        set { _rotationPeriodDays = Math.Max(1, value); OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
    }

    // ── 提醒设置 ─────────────────────────────────────────────────────────

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

    // ── 当前批次计算 ─────────────────────────────────────────────────────

    /// <summary>
    /// 根据今天日期返回当前应显示的批次，Groups 为空时返回 null。
    /// </summary>
    public RotationGroup? GetCurrentGroup()
    {
        if (Groups.Count == 0) return null;
        var daysPassed = Math.Max(0, (int)(DateTime.Today - RotationStartDate).TotalDays);
        var idx = (daysPassed / RotationPeriodDays) % Groups.Count;
        return Groups[idx];
    }

    /// <summary>给设置页显示的当前状态摘要。</summary>
    public string StatusText
    {
        get
        {
            if (Groups.Count == 0) return "（尚未创建批次）";
            var daysPassed = Math.Max(0, (int)(DateTime.Today - RotationStartDate).TotalDays);
            var idx = (daysPassed / RotationPeriodDays) % Groups.Count;
            var group = Groups[idx];
            var batchNo    = daysPassed / RotationPeriodDays;
            var batchStart = RotationStartDate.AddDays(batchNo * RotationPeriodDays);
            var batchEnd   = batchStart.AddDays(RotationPeriodDays - 1);
            var daysLeft   = (batchEnd - DateTime.Today).Days + 1;
            return $"当前：{group.Name}（{batchStart:M/d} – {batchEnd:M/d}，还剩 {daysLeft} 天）";
        }
    }

    // ── 订阅集合变更 ─────────────────────────────────────────────────────

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
