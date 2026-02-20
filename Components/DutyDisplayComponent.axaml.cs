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
    "显示当前时段的值日生信息")]
public partial class DutyDisplayComponent : ComponentBase
{
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromMinutes(1)
    };

    public DutyDisplayComponent()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Render();
        AttachedToVisualTree   += (_, _) => { Render(); _timer.Start(); };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    private void Render()
    {
        Root.Children.Clear();

        var now     = DateTime.Now;
        var weekday = ToWeekDay(now.DayOfWeek);

        if (!Plugin.Config.WeekConfig.TryGetValue(weekday, out var slots) || slots.Count == 0)
        {
            ShowEmpty("今天暂无值日配置");
            return;
        }

        // 保留 slot 引用，以便显示时段文字
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

        // 格式：  08:00–12:00       08:00–12:00
        //        打扫卫生：小明    │  擦黑板：小李
        for (int i = 0; i < activeItems.Count; i++)
        {
            var (slot, item) = activeItems[i];

            // 解析项目自定义颜色，失败则用白色
            IBrush foreground = Color.TryParse(item.Color, out var c)
                ? new SolidColorBrush(c)
                : Brushes.White;

            var names = string.Join("、",
                new[] { item.Person1, item.Person2, item.Person3 }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            var mainText = string.IsNullOrWhiteSpace(names)
                ? item.Project
                : $"{item.Project}：{names}";

            var timeLabel = $"{slot.Start:hh\\:mm}–{slot.End:hh\\:mm}";

            // 每个项目：竖向小卡片（时段标签 + 正文）
            var card = new StackPanel
            {
                Orientation       = Avalonia.Layout.Orientation.Vertical,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };

            // 左上角时段标签：不透明白色小字
            card.Children.Add(new TextBlock
            {
                Text       = timeLabel,
                FontSize   = 10,
                Foreground = Brushes.White,
                Opacity    = 1.0,
                Margin     = new Avalonia.Thickness(0, 0, 0, 1),
            });

            // 主文字：项目 + 人员，使用用户设定的颜色
            card.Children.Add(new TextBlock
            {
                Text       = mainText,
                FontSize   = 15,
                FontWeight = FontWeight.Medium,
                Foreground = foreground,
            });

            Root.Children.Add(card);

            // 分隔符 │（最后一项不加）
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

    private void ShowEmpty(string message)
    {
        Root.Children.Add(new TextBlock
        {
            Text              = message,
            Foreground        = Brushes.Gray,
            FontSize          = 14,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
    }

    private static WeekDay ToWeekDay(DayOfWeek day) => day switch
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
