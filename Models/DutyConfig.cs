using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DutyListPlugin.Models;

public class DutyConfig : INotifyPropertyChanged
{
    private Dictionary<WeekDay, ObservableCollection<DutyTimeSlot>> _weekConfig = new();

    public Dictionary<WeekDay, ObservableCollection<DutyTimeSlot>> WeekConfig
    {
        get => _weekConfig;
        set
        {
            // 取消旧集合的订阅，避免内存泄漏
            foreach (var col in _weekConfig.Values)
                col.CollectionChanged -= OnCollectionChanged;

            _weekConfig = value;

            // ✅ 修复 Bug3：订阅每个集合的 CollectionChanged，
            //    使用户在 UI 增删时间段/人员项时也能触发自动保存，
            //    而不是只有整个 WeekConfig 被整体替换时才触发。
            foreach (var col in value.Values)
                col.CollectionChanged += OnCollectionChanged;

            OnPropertyChanged();
        }
    }

    // ✅ 新增：在外部直接对 WeekConfig[key] 赋值（字典 set 但不触发属性通知）时，
    //    也需要将新集合纳入监听。调用此方法来完成注册。
    public void SubscribeCollection(ObservableCollection<DutyTimeSlot> collection)
    {
        collection.CollectionChanged -= OnCollectionChanged; // 防重复订阅
        collection.CollectionChanged += OnCollectionChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(WeekConfig));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
