using System.Windows;
using NcmdumpCSharpGui.Models;
using NcmdumpCSharpGui.ViewModels;

namespace NcmdumpCSharpGui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 當有新記錄加入時自動捲動到底部
        var vm = (MainViewModel)DataContext;
        vm.LogEntries.CollectionChanged += (_, _) =>
        {
            if (LogListBox.Items.Count > 0)
                LogListBox.ScrollIntoView(LogListBox.Items[^1]);
        };
    }
}
