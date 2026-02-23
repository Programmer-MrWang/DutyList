using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using DutyListPlugin.Models;

namespace DutyListPlugin.Notifications;

// ── Fix 1: 改为非泛型 NotificationProviderBase ─────────────────────────
// 原来 NotificationProviderBase<object> 导致框架在反射/序列化 Settings 属性时
// 静默失败，provider 无法正常注册到 NotificationHostService，
// ShowNotification 永远不会被调用。
[NotificationProviderInfo(
    "B1C2D3E4-F5A6-7890-ABCD-EF1234567890",
    "值日生提醒",
    "\uE9B0",
    "在值日时段开始时发出提醒")]
public class DutyNotificationProvider : NotificationProviderBase
{
    // ── Fix 2: 轮询代替精确调度 ──────────────────────────────────────────
    // 原来的精确调度逻辑中：
    //   .Cast<(DateTime FireAt, DutyTimeSlot Slot)?>().FirstOrDefault()
    // 对值类型元组做 nullable cast 在部分运行时会抛 InvalidCastException，
    // 导致 ScheduleNext 抛异常后整个 provider 静默失效。
    // 改为每 30 秒轮询，逻辑简单可靠，通知误差 ≤30 秒，完全可以接受。
    private DispatcherTimer? _pollTimer;

    // 记录已触发的 key，避免同一时段重复发送（key 含日期，跨天自动失效）
    private readonly HashSet<string> _firedKeys = new();
    private string _firedDate = "";

    public DutyNotificationProvider()
    {
        // 必须在 UI 线程上创建 DispatcherTimer，
        // 用 Post 确保此时 Avalonia UI 线程已就绪
        Dispatcher.UIThread.Post(Initialize, DispatcherPriority.Background);
    }

    private void Initialize()
    {
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        if (!Plugin.Config.EnableReminder) return;

        var now   = DateTime.Now;
        var today = DateTime.Today;

        // 每天第一次轮询时清空已触发集合，避免无限增长
        var dateStr = today.ToString("yyyyMMdd");
        if (_firedDate != dateStr)
        {
            _firedKeys.Clear();
            _firedDate = dateStr;
        }

        var (group, dayIndex) = Plugin.Config.GetCurrentGroupAndDay();
        if (group == null || dayIndex == 0 || !group.EnableReminder) return;
        if (!group.DayConfig.TryGetValue(dayIndex, out var slots)) return;

        foreach (var slot in slots)
        {
            var fireAt = today + slot.Start;
            var key    = $"{dateStr}_{slot.Start}_{slot.End}";

            // 在 [fireAt, fireAt+30s) 窗口内且未发过 → 发送
            if (now >= fireAt && now < fireAt.AddSeconds(30) && !_firedKeys.Contains(key))
            {
                _firedKeys.Add(key);
                FireNotification(slot);
            }
        }
    }

    // ── 发送提醒 ──────────────────────────────────────────────────────────

    private void FireNotification(DutyTimeSlot slot)
    {
        // 二次检查：防止两次轮询之间配置被关掉
        if (!Plugin.Config.EnableReminder) return;

        var (group, dayIndex) = Plugin.Config.GetCurrentGroupAndDay();
        if (group == null || dayIndex == 0 || !group.EnableReminder) return;

        var items = slot.Items
            .Where(x => !string.IsNullOrWhiteSpace(x.Project))
            .Select(x =>
            {
                var names = string.Join("、",
                    new[] { x.Person1, x.Person2, x.Person3 }
                        .Where(n => !string.IsNullOrWhiteSpace(n)));
                return string.IsNullOrWhiteSpace(names)
                    ? x.Project
                    : $"{x.Project}：{names}";
            })
            .ToList();

        if (items.Count == 0) return;

        // Fix 3: TimeSpan 格式化用 hh（原来上一版错误地改成了 HH，
        //        HH 是 DateTime 专属格式符，对 TimeSpan 会抛 FormatException）。
        var timeLabel = $"{slot.Start:hh\\:mm}–{slot.End:hh\\:mm}";
        var body      = string.Join("  |  ", items);
        var speech    = BuildSpeechContent(items);

        var mask = NotificationContent.CreateTwoIconsMask($"值日开始  {timeLabel}");
        mask.Duration = TimeSpan.FromSeconds(4);

        var overlay = NotificationContent.CreateSimpleTextContent(body);
        overlay.Duration        = TimeSpan.FromSeconds(6);
        overlay.SpeechContent   = Plugin.Config.EnableReminderSound ? speech : "";
        overlay.IsSpeechEnabled = Plugin.Config.EnableReminderSound;

        ShowNotification(new NotificationRequest
        {
            MaskContent    = mask,
            OverlayContent = overlay
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
