using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DutyListPlugin.Models;

public class DutyTimeSlot : INotifyPropertyChanged
{
    private TimeSpan _start = TimeSpan.FromHours(8);
    private TimeSpan _end   = TimeSpan.FromHours(12);

    public TimeSpan Start
    {
        get => _start;
        set
        {
            _start = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StartText));
        }
    }

    public TimeSpan End
    {
        get => _end;
        set
        {
            _end = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EndText));
        }
    }

    /// <summary>
    /// 用于 TextBox 双向绑定的时间字符串（格式 HH:mm）。
    /// </summary>
    public string StartText
    {
        get => _start.ToString(@"hh\:mm");
        set
        {
            // ✅ 修复 Bug4：原先只用 hh\:mm（要求两位数），用户输入 "8:00" 这类单位数小时时
            //    TryParseExact 会静默失败。改用 TryParse 兼容 "8:00" 和 "08:00" 等所有常见格式。
            if (TimeSpan.TryParse(value?.Trim(), out var ts))
                Start = ts;
        }
    }

    public string EndText
    {
        get => _end.ToString(@"hh\:mm");
        set
        {
            if (TimeSpan.TryParse(value?.Trim(), out var ts))
                End = ts;
        }
    }

    public ObservableCollection<DutyItem> Items { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
