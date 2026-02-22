using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared.Helpers;
using DutyListPlugin.Migration;
using DutyListPlugin.Models;

namespace DutyListPlugin.Settings;

[SettingsPageInfo("duty.list.plugin.settings", "值日生名单")]
public partial class DutySettingsPage : SettingsPageBase
{
    private RotationGroup? _currentGroup;
    private WeekDay        _currentDay = WeekDay.Monday;

    public DutySettingsPage()
    {
        InitializeComponent();

        // ── 绑定 DataContext 到 Config，让 CheckBox 直接绑定属性 ──
        DataContext = Plugin.Config;

        // ── 初始化轮换参数控件 ──
        StartDatePicker.SelectedDate = new DateTimeOffset(Plugin.Config.RotationStartDate);
        PeriodDaysSpin.Value         = Plugin.Config.RotationPeriodDays;
        PeriodDaysSpin.ValueChanged += OnPeriodDaysChanged;   // 绕过 AVLN3000，改在代码中订阅
        StatusLabel.Text             = Plugin.Config.StatusText;

        // ── 星期选择器 ──
        WeekSelector.ItemsSource   = Enum.GetValues(typeof(WeekDay));
        WeekSelector.SelectedIndex = 0;

        // ── 批次选择器 ──
        RefreshGroupSelector();
    }

    // ══════════════ 轮换参数 ══════════════════════════════════════════════

    private void OnStartDateChanged(object? sender, DatePickerSelectedValueChangedEventArgs e)
    {
        if (e.NewDate is DateTimeOffset dto)
        {
            Plugin.Config.RotationStartDate = dto.DateTime.Date;
            RefreshStatus();
        }
    }

    private void OnPeriodDaysChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        // 强制取整（FormatString="0" 只影响显示，Value 本身是 decimal）
        var days = (int)Math.Round(e.NewValue ?? 7);
        Plugin.Config.RotationPeriodDays = Math.Max(1, days);
        RefreshStatus();
    }

    private void RefreshStatus() => StatusLabel.Text = Plugin.Config.StatusText;

    // ══════════════ 批次管理 ══════════════════════════════════════════════

    private void RefreshGroupSelector()
    {
        GroupSelector.ItemsSource = null;
        GroupSelector.ItemsSource = Plugin.Config.Groups;
        GroupSelector.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(RotationGroup.Name));
    }

    private void OnGroupChanged(object? sender, SelectionChangedEventArgs e)
    {
        _currentGroup = GroupSelector.SelectedItem as RotationGroup;
        GroupEditor.IsVisible = _currentGroup != null;
        if (_currentGroup == null) return;
        RefreshStatus();
        OnWeekChanged(null, null!);
    }

    private void AddGroup(object? sender, RoutedEventArgs e)
    {
        var g = new RotationGroup { Name = $"第{Plugin.Config.Groups.Count + 1}批" };
        Plugin.Config.Groups.Add(g);
        RefreshGroupSelector();
        GroupSelector.SelectedItem = g;
        Plugin.Save();
    }

    private void RenameGroup(object? sender, RoutedEventArgs e)
    {
        if (_currentGroup == null) return;
        var idx = Plugin.Config.Groups.IndexOf(_currentGroup) + 1;
        _currentGroup.Name = $"第{idx}批";
        RefreshGroupSelector();
        GroupSelector.SelectedItem = _currentGroup;
        Plugin.Save();
    }

    private void RemoveGroup(object? sender, RoutedEventArgs e)
    {
        if (_currentGroup == null) return;
        Plugin.Config.Groups.Remove(_currentGroup);
        _currentGroup = null;
        GroupEditor.IsVisible = false;
        RefreshGroupSelector();
        if (Plugin.Config.Groups.Count > 0)
            GroupSelector.SelectedIndex = 0;
        Plugin.Save();
    }

    // ══════════════ 星期 / 时间段 / 项目 ════════════════════════════════

    private void OnWeekChanged(object? sender, SelectionChangedEventArgs? e)
    {
        if (_currentGroup == null) return;
        if (WeekSelector.SelectedItem is WeekDay day) _currentDay = day;
        EnsureDayExists();
        TimeSlotList.ItemsSource = _currentGroup.WeekConfig[_currentDay];
    }

    private void AddTimeSlot(object? sender, RoutedEventArgs e)
    {
        if (_currentGroup == null) return;
        EnsureDayExists();
        _currentGroup.WeekConfig[_currentDay].Add(new DutyTimeSlot
        {
            Start = TimeSpan.FromHours(8),
            End   = TimeSpan.FromHours(12)
        });
    }

    private void RemoveTimeSlot(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not DutyTimeSlot slot) return;
        if (_currentGroup?.WeekConfig.TryGetValue(_currentDay, out var list) == true)
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
        if (_currentGroup?.WeekConfig.TryGetValue(_currentDay, out var slots) == true)
            foreach (var slot in slots)
                if (slot.Items.Remove(item)) break;
    }

    private void Save(object? sender, RoutedEventArgs e) => Plugin.Save();

    private void EnsureDayExists()
    {
        if (_currentGroup == null) return;
        if (!_currentGroup.WeekConfig.ContainsKey(_currentDay))
        {
            var col = new ObservableCollection<DutyTimeSlot>();
            _currentGroup.WeekConfig[_currentDay] = col;
            Plugin.Config.SubscribeCollection(col);
        }
    }

    // ══════════════ 设置 Tab ═════════════════════════════════════════════

    private void OnReminderChanged(object? sender, RoutedEventArgs e)
    {
        Plugin.Config.EnableReminder = EnableReminderBox.IsChecked ?? false;
        Plugin.Save();
    }

    private void OnSoundChanged(object? sender, RoutedEventArgs e)
    {
        Plugin.Config.EnableReminderSound = EnableSoundBox.IsChecked ?? false;
        Plugin.Save();
    }

    private async void ExportConfig(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title              = "导出值日表配置",
                SuggestedFileName  = $"duty_config_{DateTime.Now:yyyyMMdd_HHmm}.json",
                FileTypeChoices    = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
            });

            if (file == null) return;

            var json = JsonSerializer.Serialize(Plugin.Config, new JsonSerializerOptions { WriteIndented = true });
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json);

            ExportResultLabel.Text      = $"✅ 已导出到 {file.Name}";
            ExportResultLabel.IsVisible = true;
        }
        catch (Exception ex)
        {
            ExportResultLabel.Text      = $"❌ 导出失败：{ex.Message}";
            ExportResultLabel.IsVisible = true;
        }
    }

    private void ResetConfig(object? sender, RoutedEventArgs e)
    {
        Plugin.Config.Groups.Clear();
        Plugin.Config.RotationStartDate  = DateTime.Today;
        Plugin.Config.RotationPeriodDays = 7;
        Plugin.Config.EnableReminder     = true;
        Plugin.Config.EnableReminderSound = true;

        RefreshGroupSelector();
        GroupEditor.IsVisible   = false;
        _currentGroup           = null;
        StatusLabel.Text        = Plugin.Config.StatusText;
        PeriodDaysSpin.Value    = 7;

        ExportResultLabel.Text      = "✅ 配置已重置";
        ExportResultLabel.IsVisible = true;

        Plugin.Save();
    }

    // ══════════════ 旧版配置导入 ══════════════════════════════════════════

    /// <summary>
    /// 手动选取旧版 duty.json 并将其迁移合并到当前配置。
    /// <para>
    /// 旧版的 WeekConfig 将被转换为一个新的 <see cref="RotationGroup"/> 并追加到
    /// <see cref="DutyConfig.Groups"/> 末尾；已有批次不受影响。
    /// </para>
    /// </summary>
    private async void ImportLegacyConfig(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            // 让用户选择旧版 duty.json 文件
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title           = "选择旧版值日表配置（duty.json）",
                AllowMultiple   = false,
                FileTypeFilter  = new[]
                {
                    new FilePickerFileType("JSON 配置") { Patterns = new[] { "*.json" } }
                }
            });

            if (files.Count == 0) return;

            // 获取本地路径
            var localPath = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(localPath))
            {
                SetImportResult(false, "无法获取文件路径，请确保文件在本地磁盘上");
                return;
            }

            // 校验是否为旧版格式
            if (!LegacyConfigMigrator.IsLegacyConfig(localPath))
            {
                SetImportResult(false, "所选文件不是旧版配置格式，请确认是否选错了文件");
                return;
            }

            // 执行迁移
            var migrated = LegacyConfigMigrator.Migrate(localPath);
            if (migrated == null || migrated.Groups.Count == 0)
            {
                SetImportResult(false, "配置文件内容为空或解析失败");
                return;
            }

            // 将迁移出的批次追加到当前配置（保留已有批次）
            int addedCount = 0;
            foreach (var group in migrated.Groups)
            {
                // 自动命名：若"第1批"已存在则改为"第N批（导入）"
                if (Plugin.Config.Groups.Count > 0)
                    group.Name = $"第{Plugin.Config.Groups.Count + 1}批（旧版导入）";

                Plugin.Config.Groups.Add(group);
                addedCount++;
            }

            RefreshGroupSelector();
            StatusLabel.Text = Plugin.Config.StatusText;
            Plugin.Save();

            SetImportResult(true, $"已成功导入 {addedCount} 个批次");
        }
        catch (Exception ex)
        {
            SetImportResult(false, $"导入出错：{ex.Message}");
        }
    }

    private void SetImportResult(bool success, string message)
    {
        // 复用设置页已有的 ExportResultLabel 显示结果
        ExportResultLabel.Text      = (success ? "✅ " : "❌ ") + message;
        ExportResultLabel.IsVisible = true;
    }
}
