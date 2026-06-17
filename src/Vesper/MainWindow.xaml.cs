using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using Vesper.Models;
using Vesper.ViewModels;

namespace Vesper;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private TaskbarIcon? _trayIcon;
    private bool _forceClose;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        CreateTrayIcon();
        viewModel.Start();
    }

    private void CreateTrayIcon()
    {
        try
        {
            _trayIcon = new TaskbarIcon();
            _trayIcon.ToolTipText = "Vesper";

            // Try loading icon from embedded resource, fall back to system icon
            try
            {
                var iconUri = new Uri("pack://application:,,,/Resources/vesper.ico", UriKind.Absolute);
                var streamInfo = Application.GetResourceStream(iconUri);
                if (streamInfo != null)
                    _trayIcon.Icon = new System.Drawing.Icon(streamInfo.Stream);
                else
                    _trayIcon.Icon = System.Drawing.SystemIcons.Application;
            }
            catch
            {
                _trayIcon.Icon = System.Drawing.SystemIcons.Application;
            }

            // Context menu
            var showItem = new MenuItem { Header = "Show" };
            showItem.Click += TrayShow_Click;
            var settingsItem = new MenuItem { Header = "Settings" };
            settingsItem.Click += TraySettings_Click;
            var exitItem = new MenuItem { Header = "Exit" };
            exitItem.Click += TrayExit_Click;

            _trayIcon.ContextMenu = new ContextMenu
            {
                Items = { showItem, settingsItem, new Separator(), exitItem }
            };

            _trayIcon.TrayMouseDoubleClick += TrayIcon_TrayMouseDoubleClick;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to create tray icon: {ex.Message}");
        }
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            ShowInTaskbar = false;
            Hide();
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        ViewModel.Dispose();
        _trayIcon?.Dispose();
    }

    private void MinimizeToTray_Click(object sender, RoutedEventArgs e)
    {
        ShowInTaskbar = false;
        Hide();
    }

    private void TrayShow_Click(object sender, RoutedEventArgs e)
    {
        ShowWindow();
    }

    private void TraySettings_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.OpenSettingsCommand.CanExecute(null))
        {
            ShowWindow();
            ViewModel.OpenSettingsCommand.Execute(null);
        }
    }

    private void TrayIcon_TrayMouseDoubleClick(object? sender, RoutedEventArgs e)
    {
        ShowWindow();
    }

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        _forceClose = true;
        Close();
    }

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
        {
            if (Enum.TryParse<RecognitionMode>(tag, out var mode))
            {
                ViewModel.CurrentMode = mode;
            }
        }
    }

    public void ShowWindow()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void StartInTray()
    {
        ShowInTaskbar = false;
        Hide();
    }
}