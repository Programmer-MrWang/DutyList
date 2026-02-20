using System;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using DutyListPlugin.Models;

namespace DutyListPlugin.Settings;

[SettingsPageInfo("duty.list.plugin.settings", "值日生名单")]
public partial class DutySettingsPage : SettingsPageBase
{
    private WeekDay _currentDay = WeekDay.Monday;

    public DutySettingsPage()
    {
        InitializeComponent();
        WeekSelector.ItemsSource   = Enum.GetValues(typeof(WeekDay));
        WeekSelector.SelectedIndex = 0;
    }

    private void OnWeekChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (WeekSelector.SelectedItem is not WeekDay day) return;
        _currentDay = day;

        if (!Plugin.Config.WeekConfig.ContainsKey(day))
        {
            var newCol = new ObservableCollection<DutyTimeSlot>();
            Plugin.Config.WeekConfig[day] = newCol;
            // ✅ 修复 Bug5：直接对字典 key 赋值不会触发 WeekConfig 的 setter，
            //    新集合不会被自动订阅 CollectionChanged，需要手动调用 SubscribeCollection。
            Plugin.Config.SubscribeCollection(newCol);
        }

        TimeSlotList.ItemsSource = Plugin.Config.WeekConfig[day];
    }

    private void AddTimeSlot(object? sender, RoutedEventArgs e)
    {
        EnsureDayExists();
        Plugin.Config.WeekConfig[_currentDay].Add(new DutyTimeSlot
        {
            Start = TimeSpan.FromHours(8),
            End   = TimeSpan.FromHours(12)
        });
    }

    private void RemoveTimeSlot(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not DutyTimeSlot slot) return;
        if (Plugin.Config.WeekConfig.TryGetValue(_currentDay, out var list))
            list.Remove(slot);
    }

    private void AddItem(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is DutyTimeSlot slot)
            slot.Items.Add(new DutyItem());
    }

    private void RemoveItem(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not DutyItem item) return;
        if (Plugin.Config.WeekConfig.TryGetValue(_currentDay, out var slots))
            foreach (var slot in slots)
                if (slot.Items.Remove(item)) break;
    }

    private void Save(object? sender, RoutedEventArgs e)
    {
        Plugin.Save();
    }

    private void EnsureDayExists()
    {
        if (!Plugin.Config.WeekConfig.ContainsKey(_currentDay))
        {
            var newCol = new ObservableCollection<DutyTimeSlot>();
            Plugin.Config.WeekConfig[_currentDay] = newCol;
            Plugin.Config.SubscribeCollection(newCol);
        }
    }
}
