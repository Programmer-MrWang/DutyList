using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DutyListPlugin.Models;

/// <summary>
/// 一个轮换批次，包含该批次的完整星期→时间段→人员配置。
/// </summary>
public class RotationGroup : INotifyPropertyChanged
{
    private string _name = "批次";

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public Dictionary<WeekDay, ObservableCollection<DutyTimeSlot>> WeekConfig { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
