using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DutyListPlugin.Models;

public class DutyItem : INotifyPropertyChanged
{
    private string _project = "";
    private string _person1 = "";
    private string _person2 = "";
    private string _person3 = "";
    private string _color   = "#00BFFF";   // 新增：每个项目可自定义字体颜色

    public string Project
    {
        get => _project;
        set { _project = value; OnPropertyChanged(); }
    }

    public string Person1
    {
        get => _person1;
        set { _person1 = value; OnPropertyChanged(); }
    }

    public string Person2
    {
        get => _person2;
        set { _person2 = value; OnPropertyChanged(); }
    }

    public string Person3
    {
        get => _person3;
        set { _person3 = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 字体颜色，格式为 "#RRGGBB" 或 "#AARRGGBB"。默认天蓝 #00BFFF。
    /// </summary>
    public string Color
    {
        get => _color;
        set { _color = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
