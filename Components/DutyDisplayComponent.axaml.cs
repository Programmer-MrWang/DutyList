using System;
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
    "\uE9B0",
    "显示当前轮换批次、当前时段的值日生信息")]
public partial class DutyDisplayComponent : ComponentBase
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };

    public DutyDisplayComponent()
    {
        InitializeComponent();
        _timer.Tick             += (_, _) => Render();
        AttachedToVisualTree    += (_, _) => { Render(); _timer.Start(); };
        DetachedFromVisualTree  += (_, _) => _timer.Stop();
    }

    private void Render()
    {
        Root.Children.Clear();

        // 1. 找当前批次
        var group = Plugin.Config.GetCurrentGroup();
        if (group is null)
        {
            ShowEmpty("暂无值日批次配置");
            return;
        }

        // 2. 找当前时段内的条目
        var now     = DateTime.Now;
        var weekday = ToWeekDay(now.DayOfWeek);

        if (!group.WeekConfig.TryGetValue(weekday, out var slots) || slots.Count == 0)
        {
            ShowEmpty("今天暂无值日配置");
            return;
        }

        var activeItems = slots
            .Where(s => s.Start <= now.TimeOfDay && now.TimeOfDay < s.End)
            .SelectMany(s => s.Items.Select(item => (Slot: s, Item: item)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Item.Project))
            .ToList();

        if (activeItems.Count == 0)
        {
            ShowEmpty("当前时段无值日任务");
            return;
        }

        // 3. 渲染：时段小标签 + 主文字，用 │ 分隔
        for (int i = 0; i < activeItems.Count; i++)
        {
            var (slot, item) = activeItems[i];

            IBrush fg = Color.TryParse(item.Color, out var c)
                ? new SolidColorBrush(c)
                : Brushes.White;

            var names = string.Join("、",
                new[] { item.Person1, item.Person2, item.Person3 }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            var mainText  = string.IsNullOrWhiteSpace(names) ? item.Project : $"{item.Project}：{names}";
            var timeLabel = $"{slot.Start:hh\\:mm}–{slot.End:hh\\:mm}";

            var card = new StackPanel
            {
                Orientation       = Avalonia.Layout.Orientation.Vertical,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };

            card.Children.Add(new TextBlock
            {
                Text       = timeLabel,
                FontSize   = 10,
                Foreground = Brushes.White,
                Margin     = new Avalonia.Thickness(0, 0, 0, 1),
            });

            card.Children.Add(new TextBlock
            {
                Text       = mainText,
                FontSize   = 15,
                FontWeight = FontWeight.Medium,
                Foreground = fg,
            });

            Root.Children.Add(card);

            if (i < activeItems.Count - 1)
            {
                Root.Children.Add(new TextBlock
                {
                    Text              = "  │  ",
                    FontSize          = 15,
                    Foreground        = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                });
            }
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

    private static WeekDay ToWeekDay(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday    => WeekDay.Monday,
        DayOfWeek.Tuesday   => WeekDay.Tuesday,
        DayOfWeek.Wednesday => WeekDay.Wednesday,
        DayOfWeek.Thursday  => WeekDay.Thursday,
        DayOfWeek.Friday    => WeekDay.Friday,
        DayOfWeek.Saturday  => WeekDay.Saturday,
        _                   => WeekDay.Sunday
    };
}
