using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Models.Notification;
using DutyListPlugin.Models;

namespace DutyListPlugin.Notifications;

/// <summary>
/// 精确调度版：预读今天所有时间段的开始时刻，到点时直接发送提醒。
/// 调度时将目标时间段随 Timer 一起存储，触发时不再做时间窗口判断，
/// 避免系统负载导致 Timer 偏移而漏发。
/// </summary>
[NotificationProviderInfo(
    "B1C2D3E4-F5A6-7890-ABCD-EF1234567890",
    "值日生提醒",
    "\uE9B0",
    "在值日时段开始时发出提醒")]
public class DutyNotificationProvider : NotificationProviderBase<object>
{
    private readonly DispatcherTimer _timer = new() { IsEnabled = false };

    // 本次调度的目标：要触发提醒的时间段 key（日期+起止），防止重复触发
    private DutyTimeSlot? _pendingSlot;
    private string        _lastFiredKey = "";

    public DutyNotificationProvider()
    {
        _timer.Tick += OnTick;
        ScheduleNext();
    }

    // ── 精确调度 ──────────────────────────────────────────────────────────

    private void ScheduleNext()
    {
        _timer.Stop();
        _pendingSlot = null;

        if (!Plugin.Config.EnableReminder) return;

        var now      = DateTime.Now;
        var midnight = DateTime.Today.AddDays(1);

        // 收集今天所有待触发的 (触发时刻, 时间段) 对
        var candidates = GetTodayStartsWithSlot();

        // 找最近的未来触发点
        var next = candidates
            .Where(x => x.FireAt > now)
            .OrderBy(x => x.FireAt)
            .Cast<(DateTime FireAt, DutyTimeSlot Slot)?>()
            .FirstOrDefault();

        if (next.HasValue)
        {
            _pendingSlot      = next.Value.Slot;
            _timer.Interval   = next.Value.FireAt - now;
            _timer.Tag        = "notify";
        }
        else
        {
            // 今天没有更多时间段，等到明天 0 点重新调度
            _timer.Interval = midnight - now;
            _timer.Tag      = "midnight";
        }

        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();

        if (_timer.Tag as string == "midnight")
        {
            ScheduleNext();
            return;
        }

        // 直接用存好的 _pendingSlot 发送提醒，不再做时间窗口判断
        if (_pendingSlot != null)
            FireNotification(_pendingSlot);

        ScheduleNext();   // 调度今天下一个时间段
    }

    // ── 收集今天所有触发点 ────────────────────────────────────────────────

    private static List<(DateTime FireAt, DutyTimeSlot Slot)> GetTodayStartsWithSlot()
    {
        var result = new List<(DateTime, DutyTimeSlot)>();
        var today  = DateTime.Today;

        var (group, dayIndex) = Plugin.Config.GetCurrentGroupAndDay();
        if (group == null || dayIndex == 0 || !group.EnableReminder) return result;

        if (!group.DayConfig.TryGetValue(dayIndex, out var slots)) return result;

        foreach (var slot in slots)
            result.Add((today + slot.Start, slot));

        return result;
    }

    // ── 发送提醒 ──────────────────────────────────────────────────────────

    private void FireNotification(DutyTimeSlot slot)
    {
        // 全局开关二次确认（用户可能在等待期间关掉了提醒）
        if (!Plugin.Config.EnableReminder) return;

        var (group, dayIndex) = Plugin.Config.GetCurrentGroupAndDay();
        if (group == null || dayIndex == 0 || !group.EnableReminder) return;

        // 防重复：同一时间段只发一次
        var key = $"{DateTime.Today:yyyyMMdd}_{slot.Start}_{slot.End}";
        if (key == _lastFiredKey) return;
        _lastFiredKey = key;

        var items = slot.Items
            .Where(x => !string.IsNullOrWhiteSpace(x.Project))
            .Select(x =>
            {
                var names = string.Join("、",
                    new[] { x.Person1, x.Person2, x.Person3 }
                    .Where(n => !string.IsNullOrWhiteSpace(n)));
                return string.IsNullOrWhiteSpace(names) ? x.Project : $"{x.Project}：{names}";
            }).ToList();

        if (items.Count == 0) return;

        var timeLabel = $"{slot.Start:hh\\:mm}–{slot.End:hh\\:mm}";
        var body      = string.Join("  |  ", items);
        var speech    = BuildSpeechContent(items);

        ShowNotification(new NotificationRequest
        {
            MaskContent = NotificationContent.CreateTwoIconsMask(
                $"值日开始  {timeLabel}",
                factory: x => x.Duration = TimeSpan.FromSeconds(4)),

            OverlayContent = NotificationContent.CreateRollingTextContent(
                body,
                factory: x =>
                {
                    x.Duration        = TimeSpan.FromSeconds(6);
                    x.SpeechContent   = Plugin.Config.EnableReminderSound ? speech : "";
                    x.IsSpeechEnabled = Plugin.Config.EnableReminderSound;
                })
        });
    }

    private static string BuildSpeechContent(List<string> items)
    {
        var text  = $"值日提醒：{string.Join("，", items)}";
        var voice = Plugin.Config.TtsVoice;
        if (string.IsNullOrWhiteSpace(voice)) return text;
        return $"<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\">" +
               $"<voice name=\"{System.Security.SecurityElement.Escape(voice)}\">" +
               $"{System.Security.SecurityElement.Escape(text)}</voice></speak>";
    }
}
