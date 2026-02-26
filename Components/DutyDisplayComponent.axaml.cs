using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using DutyListPlugin.Models;

namespace DutyListPlugin.Components;

[ComponentInfo(
    "68F4A3B2-C1D5-4E7A-9F0B-2A6E3D8C1B45",
    "值日生名单",
    "\uEC4E",
    "显示当前轮换批次、当前时段的值日生信息")]
public partial class DutyDisplayComponent : ComponentBase
{
    // 精确调度：每次只设置一个单次 Timer，到点后重新调度下一个触发时刻
    private readonly DispatcherTimer _preciseTimer = new() { IsEnabled = false };

    public DutyDisplayComponent()
    {
        InitializeComponent();
        _preciseTimer.Tick += OnPreciseTick;

        AttachedToVisualTree += (_, _) =>
        {
            Plugin.DisplayRefreshRequested += OnExternalRefresh;
            Render();
            ScheduleNext();
        };

        DetachedFromVisualTree += (_, _) =>
        {
            Plugin.DisplayRefreshRequested -= OnExternalRefresh;
            _preciseTimer.Stop();
        };
    }

    // ── 精确调度逻辑 ─────────────────────────────────────────────────────

    /// <summary>
    /// 计算今天（和明天 0 点）所有需要刷新显示的时刻，
    /// 找出下一个距今最近的时刻，设定 Timer 在那一刻触发。
    /// </summary>
    private void ScheduleNext()
    {
        _preciseTimer.Stop();

        var now        = DateTime.Now;
        var triggers   = GetTodayTriggers();          // 今天所有触发时刻
        var midnight   = DateTime.Today.AddDays(1);   // 明天 0 点（跨天重新调度）

        // 合并所有触发时刻（含明天 0 点），找最近的未来时刻
        var next = triggers
            .Append(midnight)
            .Where(t => t > now)
            .OrderBy(t => t)
            .FirstOrDefault();

        if (next == default) return;  // 今天没有更多触发点，等明天 0 点
        if (next == midnight)
        {
            // 只等到明天 0 点，到点后重新调度（不 Render）
            _preciseTimer.Interval = midnight - now;
            _preciseTimer.Tag      = "midnight";
            _preciseTimer.Start();
            return;
        }

        _preciseTimer.Interval = next - now;
        _preciseTimer.Tag      = "render";
        _preciseTimer.Start();
    }

    private void OnPreciseTick(object? sender, EventArgs e)
    {
        _preciseTimer.Stop();

        if (_preciseTimer.Tag as string == "midnight")
        {
            // 跨天：重新调度，今天不渲染（ScheduleNext 会拿到新的今日配置）
            ScheduleNext();
            return;
        }

        Render();
        ScheduleNext();   // 渲染完立即调度下一个触发点
    }

    /// <summary>
    /// 返回今天所有需要刷新显示的时刻：
    ///   每个时间段的 Start（进入时段）和 End（离开时段）都需要刷新。
    /// </summary>
    private static List<DateTime> GetTodayTriggers()
    {
        var result = new List<DateTime>();
        var today  = DateTime.Today;

        var (group, dayIndex) = Plugin.Config.GetCurrentGroupAndDay();
        if (group == null || dayIndex == 0) return result;

        if (!group.DayConfig.TryGetValue(dayIndex, out var slots)) return result;

        foreach (var slot in slots)
        {
            result.Add(today + slot.Start);
            result.Add(today + slot.End);
        }
        return result;
    }

    // ── 外部刷新（手动刷新按钮）────────────────────────────────────────

    private void OnExternalRefresh()
    {
        Render();
        ScheduleNext();   // 配置可能变了，重新调度
    }

    // ── 渲染 ─────────────────────────────────────────────────────────────

    private void Render()
    {
        Root.Children.Clear();

        var (group, dayIndex) = Plugin.Config.GetCurrentGroupAndDay();
        if (group is null)  { ShowEmpty("暂无值日批次配置"); return; }
        if (dayIndex == 0)  { ShowEmpty("今日休息");         return; }

        if (!group.DayConfig.TryGetValue(dayIndex, out var slots) || slots.Count == 0)
        {
            ShowEmpty("今天暂无值日配置");
            return;
        }

        var now = DateTime.Now;
        var activeItems = slots
            .Where(s => s.Start <= now.TimeOfDay && now.TimeOfDay < s.End)
            .SelectMany(s => s.Items.Select(item => (Slot: s, Item: item)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Item.Project))
            .ToList();

        if (activeItems.Count == 0) { ShowEmpty("当前时段无值日任务"); return; }

        for (int i = 0; i < activeItems.Count; i++)
        {
            var (slot, item) = activeItems[i];

            IBrush fg = Color.TryParse(item.Color, out var c)
                ? new SolidColorBrush(c)
                : Brushes.White;

            var names    = string.Join("、",
                new[] { item.Person1, item.Person2, item.Person3 }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            var mainText = string.IsNullOrWhiteSpace(names)
                ? item.Project
                : $"{item.Project}：{names}";

            var card = new StackPanel
            {
                Orientation       = Avalonia.Layout.Orientation.Vertical,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            card.Children.Add(new TextBlock
            {
                Text       = $"{slot.Start:hh\\:mm}–{slot.End:hh\\:mm}",
                FontSize   = 10,
                Foreground = Brushes.White,
                Margin     = new Avalonia.Thickness(0, 0, 0, 1)
            });
            card.Children.Add(new TextBlock
            {
                Text       = mainText,
                FontSize   = 15,
                FontWeight = FontWeight.Medium,
                Foreground = fg
            });
            Root.Children.Add(card);

            if (i < activeItems.Count - 1)
                Root.Children.Add(new TextBlock
                {
                    Text              = "  │  ",
                    FontSize          = 15,
                    Foreground        = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                });
        }
    }

    private void ShowEmpty(string msg) =>
        Root.Children.Add(new TextBlock
        {
            Text              = msg,
            Foreground        = Brushes.Gray,
            FontSize          = 14,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
}
