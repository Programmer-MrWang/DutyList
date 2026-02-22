using System;
using System.Linq;
using Avalonia.Threading;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Models.Notification;
using DutyListPlugin.Models;

namespace DutyListPlugin.Notifications;

/// <summary>
/// 在值日时段开始时发出提醒。
/// </summary>
[NotificationProviderInfo(
    "B1C2D3E4-F5A6-7890-ABCD-EF1234567890",
    "值日生提醒",
    "\uE9B0",
    "在值日时段开始时发出提醒")]
public class DutyNotificationProvider : NotificationProviderBase<object>
{
    private readonly DispatcherTimer _timer;

    // 记录上次触发的时间段 Key（日期+时段起止），防止同一时段多次触发
    private string _lastFiredKey = "";

    public DutyNotificationProvider()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!Plugin.Config.EnableReminder) return;

        var group = Plugin.Config.GetCurrentGroup();
        if (group is null) return;

        var now     = DateTime.Now;
        var weekday = ToWeekDay(now.DayOfWeek);

        if (!group.WeekConfig.TryGetValue(weekday, out var slots)) return;

        foreach (var slot in slots)
        {
            // 触发条件：当前时刻 在 [Start, Start+1min) 窗口内
            var diff = (now.TimeOfDay - slot.Start).TotalSeconds;
            if (diff < 0 || diff >= 60) continue;

            // 去重 key：日期 + 起止时间
            var key = $"{now:yyyyMMdd}_{slot.Start}_{slot.End}";
            if (key == _lastFiredKey) continue;
            _lastFiredKey = key;

            // 构建提醒文字
            var items = slot.Items
                .Where(x => !string.IsNullOrWhiteSpace(x.Project))
                .Select(x =>
                {
                    var names = string.Join("、",
                        new[] { x.Person1, x.Person2, x.Person3 }
                        .Where(n => !string.IsNullOrWhiteSpace(n)));
                    return string.IsNullOrWhiteSpace(names) ? x.Project : $"{x.Project}：{names}";
                })
                .ToList();

            if (items.Count == 0) continue;

            var timeLabel = $"{slot.Start:hh\\:mm}–{slot.End:hh\\:mm}";
            var body      = string.Join("  |  ", items);
            var speech    = $"值日提醒：{string.Join("，", items)}";

            ShowNotification(new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask(
                    $"值日开始  {timeLabel}",
                    factory: x => x.Duration = TimeSpan.FromSeconds(4)),

                OverlayContent = NotificationContent.CreateRollingTextContent(
                    body,
                    factory: x =>
                    {
                        x.Duration       = TimeSpan.FromSeconds(6);
                        x.SpeechContent  = Plugin.Config.EnableReminderSound ? speech : "";
                        x.IsSpeechEnabled = Plugin.Config.EnableReminderSound;
                    })
            });

            break; // 每次最多发一个
        }
    }

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
