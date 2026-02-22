using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared.Helpers;
using DutyListPlugin.Migration;
using DutyListPlugin.Models;

namespace DutyListPlugin.Settings;

// ── 天数表格行数据模型 ─────────────────────────────────────────────────────
public class DayItem
{
    public int    Index        { get; set; }
    public string Label        { get; set; } = "";
    public string Summary      { get; set; } = "";
    /// <summary>今天对应的行用淡金色背景高亮</summary>
    public string TodayBg      { get; set; } = "Transparent";
    public string TodayWeight  { get; set; } = "Normal";
}

[SettingsPageInfo("duty.list.plugin.settings", "DutyList 设置")]
public partial class DutySettingsPage : SettingsPageBase
{
    private RotationGroup? _currentGroup;
    private int            _currentDayIndex  = 1;
    private bool           _skipUpdating     = false;

    // 各周几对应的 CheckBox Name → DayOfWeek 映射（顺序与 AXAML 一致）
    private static readonly (DayOfWeek Day, string CnName)[] DayMap =
    {
        (DayOfWeek.Monday,    "周一"),
        (DayOfWeek.Tuesday,   "周二"),
        (DayOfWeek.Wednesday, "周三"),
        (DayOfWeek.Thursday,  "周四"),
        (DayOfWeek.Friday,    "周五"),
        (DayOfWeek.Saturday,  "周六"),
        (DayOfWeek.Sunday,    "周日"),
    };

    public DutySettingsPage()
    {
        InitializeComponent();
        DataContext = Plugin.Config;

        StartDatePicker.SelectedDate = new DateTimeOffset(Plugin.Config.RotationStartDate);
        PeriodDaysSpin.Value         = Plugin.Config.RotationPeriodDays;
        PeriodDaysSpin.ValueChanged  += OnPeriodDaysChanged;
        StatusLabel.Text             = Plugin.Config.StatusText;

        RefreshGroupSelector();
    }

    // ════════════════════ 轮换参数 ════════════════════════════════════════

    private void OnStartDateChanged(object? s, DatePickerSelectedValueChangedEventArgs e)
    {
        if (e.NewDate is DateTimeOffset dto)
        {
            Plugin.Config.RotationStartDate = dto.DateTime.Date;
            RefreshStatus();
        }
    }

    private void OnPeriodDaysChanged(object? s, NumericUpDownValueChangedEventArgs e)
    {
        Plugin.Config.RotationPeriodDays = Math.Max(1, (int)Math.Round(e.NewValue ?? 7));
        if (_currentGroup != null) RefreshDayTable();
        RefreshStatus();
    }

    private void RefreshStatus() => StatusLabel.Text = Plugin.Config.StatusText;

    /// <summary>展开/折叠动画面板（通过 MaxHeight + Opacity 过渡实现）。</summary>
    private static void SetPanel(Border wrapper, bool show)
    {
        wrapper.MaxHeight = show ? 9999 : 0;
        wrapper.Opacity   = show ? 1    : 0;
    }

    private void OnRefreshDisplay(object? s, RoutedEventArgs e)
    {
        Plugin.Save();
        Plugin.TriggerDisplayRefresh();
        RefreshStatus();

        // 按钮动效：图标变化 1.5s 后恢复
        RefreshDisplayBtn.IsEnabled = false;
        RefreshIcon.Text  = "⟳";
        RefreshLabel.Text = "已刷新";
        var t = new System.Timers.Timer(1500) { AutoReset = false };
        t.Elapsed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            RefreshDisplayBtn.IsEnabled = true;
            RefreshIcon.Text  = "↻";
            RefreshLabel.Text = "立即刷新";
        });
        t.Start();
    }

    // ════════════════════ 批次管理 ════════════════════════════════════════

    private void RefreshGroupSelector()
    {
        GroupSelector.ItemsSource = null;
        GroupSelector.ItemsSource = Plugin.Config.Groups;
        GroupSelector.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(RotationGroup.Name));
    }

    private void OnGroupChanged(object? s, SelectionChangedEventArgs e)
    {
        _currentGroup = GroupSelector.SelectedItem as RotationGroup;
        SetPanel(GroupEditorWrap, _currentGroup != null);
        SetPanel(DayEditorWrap, false);
        if (_currentGroup == null) return;

        GroupReminderBox.IsChecked = _currentGroup.EnableReminder;
        SyncSkipCheckboxes();
        RefreshDayTable();
        RefreshStatus();
    }

    private void AddGroup(object? s, RoutedEventArgs e)
    {
        var g = new RotationGroup { Name = $"第{Plugin.Config.Groups.Count + 1}批" };
        Plugin.Config.Groups.Add(g);
        RefreshGroupSelector();
        GroupSelector.SelectedItem = g;
        Plugin.Save();
    }

    private void RenameGroup(object? s, RoutedEventArgs e)
    {
        if (_currentGroup == null) return;
        _currentGroup.Name = $"第{Plugin.Config.Groups.IndexOf(_currentGroup) + 1}批";
        RefreshGroupSelector();
        GroupSelector.SelectedItem = _currentGroup;
        Plugin.Save();
    }

    private void RemoveGroup(object? s, RoutedEventArgs e)
    {
        if (_currentGroup == null) return;
        Plugin.Config.Groups.Remove(_currentGroup);
        _currentGroup = null;
        SetPanel(GroupEditorWrap, false);
        SetPanel(DayEditorWrap, false);
        RefreshGroupSelector();
        if (Plugin.Config.Groups.Count > 0) GroupSelector.SelectedIndex = 0;
        Plugin.Save();
    }

    private void OnGroupReminderChanged(object? s, RoutedEventArgs e)
    {
        if (_currentGroup == null) return;
        _currentGroup.EnableReminder = GroupReminderBox.IsChecked ?? true;
        Plugin.Save();
    }

    // ════════════════════ 跳过日期（多选）════════════════════════════════

    private CheckBox[] SkipCheckboxes => new[] { SkipMon, SkipTue, SkipWed, SkipThu, SkipFri, SkipSat, SkipSun };

    private void SyncSkipCheckboxes()
    {
        if (_currentGroup == null) return;
        _skipUpdating = true;
        var boxes = SkipCheckboxes;
        for (int i = 0; i < DayMap.Length; i++)
            boxes[i].IsChecked = _currentGroup.SkipDays.Contains(DayMap[i].Day);
        _skipUpdating = false;
        UpdateSkipDaysLabel();
    }

    private void OnSkipDayChanged(object? s, RoutedEventArgs e)
    {
        if (_currentGroup == null || _skipUpdating) return;
        var skip  = new List<DayOfWeek>();
        var boxes = SkipCheckboxes;
        for (int i = 0; i < DayMap.Length; i++)
            if (boxes[i].IsChecked == true) skip.Add(DayMap[i].Day);
        _currentGroup.SkipDays = skip;
        UpdateSkipDaysLabel();
        RefreshDayTable();   // 跳过日期变了，今天高亮行可能变化
        Plugin.Save();
    }

    private void UpdateSkipDaysLabel()
    {
        if (_currentGroup == null || _currentGroup.SkipDays.Count == 0)
        {
            SkipDaysLabel.Text = "（不跳过）";
            return;
        }
        // 按周一→周日顺序排列选中的名字
        var names = DayMap
            .Where(d => _currentGroup.SkipDays.Contains(d.Day))
            .Select(d => d.CnName);
        SkipDaysLabel.Text = string.Join("、", names);
    }

    // ════════════════════ 天数配置总览表 ══════════════════════════════════

    private void RefreshDayTable()
    {
        if (_currentGroup == null) return;

        var period      = Plugin.Config.RotationPeriodDays;
        var (_, todayIdx) = Plugin.Config.GetCurrentGroupAndDay();
        var items       = new List<DayItem>();

        for (int i = 1; i <= period; i++)
        {
            string summary;
            if (_currentGroup.DayConfig.TryGetValue(i, out var slots) && slots.Count > 0)
            {
                var parts = slots.Select(slot =>
                {
                    var timeStr  = $"{slot.Start:hh\\:mm}–{slot.End:hh\\:mm}";
                    var assigned = slot.Items
                        .Where(x => !string.IsNullOrWhiteSpace(x.Project))
                        .Select(x =>
                        {
                            var persons = string.Join(" ",
                                new[] { x.Person1, x.Person2, x.Person3 }
                                .Where(p => !string.IsNullOrWhiteSpace(p)));
                            return string.IsNullOrWhiteSpace(persons)
                                ? x.Project
                                : $"{x.Project}:{persons}";
                        });
                    return $"{timeStr}  {string.Join("  |  ", assigned)}";
                });
                summary = string.Join("   /   ", parts);
            }
            else
            {
                summary = "（未配置）";
            }

            bool isToday = i == todayIdx && todayIdx > 0;
            items.Add(new DayItem
            {
                Index       = i,
                Label       = $"第{i}天",
                Summary     = summary,
                TodayBg     = isToday ? "#20FFD700" : "Transparent",
                TodayWeight = isToday ? "Bold"      : "Normal",
            });
        }

        DayTableRows.ItemsSource = items;
    }

    // ════════════════════ 内联编辑面板 ════════════════════════════════════

    private void OnEditDay(object? s, RoutedEventArgs e)
    {
        if ((s as Button)?.Tag is not DayItem item) return;
        _currentDayIndex    = item.Index;
        EditorDayLabel.Text = $"编辑  第{item.Index}天";
        EnsureDayExists();
        TimeSlotList.ItemsSource = _currentGroup?.DayConfig[_currentDayIndex];
        SetPanel(DayEditorWrap, true);
    }

    private void CloseEditor(object? s, RoutedEventArgs e)
    {
        SetPanel(DayEditorWrap, false);
        RefreshDayTable();   // 关闭时刷新摘要
    }

    private void EnsureDayExists()
    {
        if (_currentGroup == null) return;
        if (!_currentGroup.DayConfig.ContainsKey(_currentDayIndex))
        {
            var col = new ObservableCollection<DutyTimeSlot>();
            _currentGroup.DayConfig[_currentDayIndex] = col;
            Plugin.Config.SubscribeCollection(col);
        }
    }

    private void AddTimeSlot(object? s, RoutedEventArgs e)
    {
        if (_currentGroup == null) return;
        EnsureDayExists();
        _currentGroup.DayConfig[_currentDayIndex].Add(new DutyTimeSlot
        {
            Start = TimeSpan.FromHours(8),
            End   = TimeSpan.FromHours(12)
        });
    }

    private void RemoveTimeSlot(object? s, RoutedEventArgs e)
    {
        if ((s as Button)?.Tag is not DutyTimeSlot slot) return;
        if (_currentGroup?.DayConfig.TryGetValue(_currentDayIndex, out var list) == true)
            list.Remove(slot);
    }

    private void AddItem(object? s, RoutedEventArgs e)
    {
        if ((s as Button)?.Tag is DutyTimeSlot slot)
            slot.Items.Add(new DutyItem());
    }

    private void RemoveItem(object? s, RoutedEventArgs e)
    {
        if ((s as Button)?.Tag is not DutyItem item) return;
        if (_currentGroup?.DayConfig.TryGetValue(_currentDayIndex, out var slots) == true)
            foreach (var slot in slots)
                if (slot.Items.Remove(item)) break;
    }

    private void Save(object? s, RoutedEventArgs e)
    {
        Plugin.Save();
        RefreshDayTable();
    }

    // ════════════════════ 设置 Tab ════════════════════════════════════════

    private void OnReminderChanged(object? s, RoutedEventArgs e)
    {
        Plugin.Config.EnableReminder = EnableReminderBox.IsChecked ?? false;
        Plugin.Save();
    }

    private void OnSoundChanged(object? s, RoutedEventArgs e)
    {
        Plugin.Config.EnableReminderSound = EnableSoundBox.IsChecked ?? false;
        Plugin.Save();
    }

    private void OnTtsVoiceChanged(object? s, RoutedEventArgs e)
    {
        Plugin.Config.TtsVoice = TtsVoiceBox.Text?.Trim() ?? "";
        Plugin.Save();
    }

    private void OnTtsPresetPicked(object? s, SelectionChangedEventArgs e)
    {
        if (TtsPresetPicker.SelectedItem is ComboBoxItem _cbi && _cbi.Tag is string voice)
        {
            Plugin.Config.TtsVoice = voice;
            TtsVoiceBox.Text       = voice;
            TtsPresetPicker.SelectedIndex = -1;
            Plugin.Save();
        }
    }

    // ════════════════════ 数据管理 ════════════════════════════════════════

    private async void ExportConfig(object? s, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title             = "导出值日表配置",
                SuggestedFileName = $"duty_config_{DateTime.Now:yyyyMMdd_HHmm}.json",
                FileTypeChoices   = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
            });
            if (file == null) return;

            var json = JsonSerializer.Serialize(Plugin.Config, new JsonSerializerOptions { WriteIndented = true });
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json);

            SetDataOpResult(true, $"已导出到 {file.Name}");
        }
        catch (Exception ex) { SetDataOpResult(false, $"导出失败：{ex.Message}"); }
    }

    private async void ImportNewConfig(object? s, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title          = "选择新版值日表配置（duty_config_*.json）",
                AllowMultiple  = false,
                FileTypeFilter = new[] { new FilePickerFileType("JSON 配置") { Patterns = new[] { "*.json" } } }
            });
            if (files.Count == 0) return;

            var localPath = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(localPath)) { SetDataOpResult(false, "无法获取文件路径"); return; }

            var text = await File.ReadAllTextAsync(localPath);
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("Groups", out _))
            {
                SetDataOpResult(false, "所选文件不是新版配置格式，请用[导入旧版]按钮导入旧版 duty.json");
                return;
            }

            var imported = JsonSerializer.Deserialize<DutyConfig>(text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (imported == null || imported.Groups.Count == 0)
            {
                SetDataOpResult(false, "配置内容为空或解析失败");
                return;
            }

            var existNames = Plugin.Config.Groups.Select(g => g.Name).ToHashSet();
            int added = 0;
            foreach (var group in imported.Groups)
            {
                if (existNames.Contains(group.Name))
                    group.Name = $"{group.Name}（导入）";
                Plugin.Config.Groups.Add(group);
                added++;
            }

            RefreshGroupSelector();
            RefreshStatus();
            Plugin.Save();
            SetDataOpResult(true, $"已成功导入 {added} 个批次");
        }
        catch (Exception ex) { SetDataOpResult(false, $"导入出错：{ex.Message}"); }
    }

    private async void ImportLegacyConfig(object? s, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title          = "选择旧版值日表配置（duty.json）",
                AllowMultiple  = false,
                FileTypeFilter = new[] { new FilePickerFileType("JSON 配置") { Patterns = new[] { "*.json" } } }
            });
            if (files.Count == 0) return;

            var localPath = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(localPath)) { SetDataOpResult(false, "无法获取文件路径"); return; }

            if (!LegacyConfigMigrator.IsLegacyConfig(localPath))
            {
                SetDataOpResult(false, "所选文件不是旧版配置格式");
                return;
            }

            var migrated = LegacyConfigMigrator.Migrate(localPath);
            if (migrated == null || migrated.Groups.Count == 0)
            {
                SetDataOpResult(false, "配置内容为空或解析失败");
                return;
            }

            int added = 0;
            foreach (var group in migrated.Groups)
            {
                if (Plugin.Config.Groups.Count > 0)
                    group.Name = $"第{Plugin.Config.Groups.Count + 1}批（旧版导入）";
                Plugin.Config.Groups.Add(group);
                added++;
            }

            RefreshGroupSelector();
            RefreshStatus();
            Plugin.Save();
            SetDataOpResult(true, $"已成功导入 {added} 个批次");
        }
        catch (Exception ex) { SetDataOpResult(false, $"导入出错：{ex.Message}"); }
    }

    private void ResetConfig(object? s, RoutedEventArgs e)
    {
        Plugin.Config.Groups.Clear();
        Plugin.Config.RotationStartDate   = DateTime.Today;
        Plugin.Config.RotationPeriodDays  = 7;
        Plugin.Config.EnableReminder      = true;
        Plugin.Config.EnableReminderSound = true;
        Plugin.Config.TtsVoice            = "";

        _currentGroup            = null;
        SetPanel(GroupEditorWrap, false);
        SetPanel(DayEditorWrap, false);
        RefreshGroupSelector();
        PeriodDaysSpin.Value = 7;
        TtsVoiceBox.Text     = "";
        StatusLabel.Text     = Plugin.Config.StatusText;
        SetDataOpResult(true, "配置已重置");
        Plugin.Save();
    }

    private void SetDataOpResult(bool ok, string msg)
    {
        DataOpResultLabel.Text      = (ok ? "✓ " : "✗ ") + msg;
        DataOpResultLabel.IsVisible = true;
    }
}
