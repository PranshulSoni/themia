using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Interop;
using Themia.App.Services;
using Themia.Core.Model;
using Windows.Graphics;

namespace Themia.App.Shell;

/// <summary>
/// The settings window: an ordinary Mica-backed Fluent window, deliberately unlike the widget
/// surface. Edits are applied live — there is no OK button, because every setting here is a look
/// the user wants to see change as they move the slider.
/// </summary>
internal sealed class SettingsWindow
{
    private readonly AppHost _host;
    private readonly Window _window;
    private readonly StackPanel _viewList;
    private nint _hwnd;

    public SettingsWindow(AppHost host)
    {
        _host = host;
        _viewList = new StackPanel { Spacing = 4 };

        var body = new StackPanel { Spacing = 4, Margin = new Thickness(24) };
        body.Children.Add(new TextBlock
        {
            Text = "Themia",
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        });

        BuildAppearance(body);
        BuildLayoutSection(body);
        BuildViewsSection(body);
        BuildDisplaysSection(body);
        BuildSystemSection(body);
        BuildAbout(body);

        _window = new Window
        {
            Title = "Themia settings",
            Content = new ScrollViewer
            {
                Content = body,
                HorizontalScrollMode = ScrollMode.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
        };

        ConfigureWindow();
        RefreshViewList();
        _host.StateChanged += RefreshViewList;
    }

    /// <summary>Raised once the window has closed, so the host can drop its reference.</summary>
    public event Action? Closed;

    private void ConfigureWindow()
    {
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));

        // AppWindow sizes are physical pixels, so a fixed 720x900 would be a postage stamp on a
        // 4K display at 150%. Scale the intended DIP size by the window's own DPI.
        var dpi = Win32.GetDpiForWindow(_hwnd);
        var scale = dpi > 0 ? dpi / 96.0 : 1.0;
        appWindow.Resize(new SizeInt32((int)Math.Round(720 * scale), (int)Math.Round(900 * scale)));
        appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "themia.ico"));


        // Mica: the standard backdrop for a Windows settings surface.
        _window.SystemBackdrop = new MicaBackdrop();

        if (_window.Content is FrameworkElement root)
        {
            root.RequestedTheme = _host.State.Design.Theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }

        _window.Closed += (_, _) =>
        {
            _host.StateChanged -= RefreshViewList;
            Closed?.Invoke();
        };
    }

    public void Activate() => _window.Activate();

    public void Close() => _window.Close();

    public void BringToFront()
    {
        _window.Activate();
        Win32.SetForegroundWindow(_hwnd);
    }

    // -----------------------------------------------------------------------------------------

    private void BuildAppearance(Panel body)
    {
        body.Children.Add(FluentLayout.Header("Appearance"));

        body.Children.Add(FluentLayout.Row("", "Theme", "Follows Windows unless you pick one",
            FluentLayout.Choice(
                [(AppTheme.System, "Use system setting"), (AppTheme.Light, "Light"), (AppTheme.Dark, "Dark")],
                _host.State.Design.Theme,
                value => _host.Mutate(state => state.Design.Theme = value))));

        body.Children.Add(FluentLayout.Row("", "Corner radius", null,
            FluentLayout.Slider(_host.State.Design.CornerRadiusPx, 0, 24, 1, "px",
                value => _host.Mutate(state => state.Design.CornerRadiusPx = value))));

        body.Children.Add(FluentLayout.Row("", "Widget opacity", null,
            FluentLayout.Slider(_host.State.Design.SurfaceOpacity * 100, 20, 100, 5, "%",
                value => _host.Mutate(state => state.Design.SurfaceOpacity = value / 100))));
    }

    private void BuildLayoutSection(Panel body)
    {
        body.Children.Add(FluentLayout.Header("Layout"));

        body.Children.Add(FluentLayout.Row("", "Gap between widgets", null,
            FluentLayout.Slider(_host.State.Design.WidgetGapPx, 0, 48, 2, "px",
                value => _host.Mutate(state => state.Design.WidgetGapPx = value))));

        body.Children.Add(FluentLayout.Row("", "Snap to grid",
            "Hold Alt while dragging to place freely",
            FluentLayout.Toggle(_host.State.Design.GridSnap,
                value => _host.Mutate(state => state.Design.GridSnap = value))));

        var editToggle = FluentLayout.Toggle(_host.EditMode, _host.SetEditMode);
        _host.EditModeChanged += editing => editToggle.IsOn = editing;

        body.Children.Add(FluentLayout.Row("", "Edit layout",
            "Show the drag handles and the snap grid on the desktop", editToggle));
    }

    private void BuildViewsSection(Panel body)
    {
        body.Children.Add(FluentLayout.Header("Views"));
        body.Children.Add(_viewList);

        var add = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new FontIcon { Glyph = "", FontSize = 14 }, new TextBlock { Text = "New view" } },
            },
            Margin = new Thickness(0, 4, 0, 0),
        };

        add.Click += (_, _) => _host.Mutate(state =>
        {
            var created = ViewState.Create($"View {state.Views.Count + 1}");
            state.Views.Add(created);
            state.ActiveViewId = created.Id;
        });

        body.Children.Add(add);
    }

    private void RefreshViewList()
    {
        _viewList.Children.Clear();
        var activeId = _host.State.ActiveView().Id;

        foreach (var view in _host.State.Views)
        {
            var id = view.Id;

            var name = new TextBox { Text = view.Name, MinWidth = 160 };
            name.LostFocus += (_, _) => _host.Mutate(state =>
            {
                if (state.FindView(id) is { } target && !string.IsNullOrWhiteSpace(name.Text))
                {
                    target.Name = name.Text.Trim();
                }
            });

            var activate = new RadioButton
            {
                IsChecked = id == activeId,
                GroupName = "ActiveView",
                MinWidth = 0,
            };

            activate.Checked += (_, _) => _host.SwitchView(id);

            var delete = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 14 },
                // The last view cannot go: a desktop with no view has nowhere to put a widget.
                IsEnabled = _host.State.Views.Count > 1,
            };

            delete.Click += (_, _) => _host.Mutate(state =>
            {
                if (state.Views.Count <= 1)
                {
                    return;
                }

                state.Views.RemoveAll(v => v.Id == id);

                foreach (var monitor in state.MonitorViews.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList())
                {
                    state.MonitorViews.Remove(monitor);
                }

                if (state.ActiveViewId == id)
                {
                    state.ActiveViewId = state.Views[0].Id;
                }
            });

            var controls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { activate, name, delete },
            };

            _viewList.Children.Add(FluentLayout.Row(
                "",
                $"{view.Name}",
                $"{view.Widgets.Count} widget{(view.Widgets.Count == 1 ? string.Empty : "s")}",
                controls));
        }
    }

    /// <summary>
    /// Which view each display shows. Without this, every monitor renders the active view, so a
    /// two-monitor desktop shows the same clock twice with no way to say otherwise.
    /// </summary>
    private void BuildDisplaysSection(Panel body)
    {
        var surfaces = _host.Surfaces;
        if (surfaces.Count < 2)
        {
            return; // nothing to choose between on a single display
        }

        body.Children.Add(FluentLayout.Header("Displays"));

        foreach (var surface in surfaces)
        {
            var monitorId = surface.Monitor.DeviceName;

            var options = new List<(string Value, string Label)> { (string.Empty, "Follow the active view") };
            options.AddRange(_host.State.Views.Select(v => (v.Id, v.Name)));

            var selected = _host.State.MonitorViews.TryGetValue(monitorId, out var assigned) ? assigned : string.Empty;

            var combo = new ComboBox { MinWidth = 200 };
            foreach (var (value, label) in options)
            {
                combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
            }

            combo.SelectedIndex = Math.Max(0, options.FindIndex(o => o.Value == selected));
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is not ComboBoxItem { Tag: string viewId })
                {
                    return;
                }

                _host.Mutate(state =>
                {
                    if (string.IsNullOrEmpty(viewId))
                    {
                        state.MonitorViews.Remove(monitorId);
                    }
                    else
                    {
                        state.MonitorViews[monitorId] = viewId;
                    }
                });
            };

            body.Children.Add(FluentLayout.Row(
                "",
                surface.Monitor.IsPrimary ? $"{monitorId} (primary)" : monitorId,
                $"{surface.Monitor.WorkArea.Width} x {surface.Monitor.WorkArea.Height}",
                combo));
        }
    }

    private void BuildSystemSection(Panel body)
    {
        body.Children.Add(FluentLayout.Header("System"));

        var startup = FluentLayout.Toggle(StartupRegistration.IsEnabled(), value =>
        {
            // Reflect what the registry actually ended up at, so a failed write does not leave the
            // toggle claiming something untrue.
            var achieved = StartupRegistration.SetEnabled(value);
            _host.Mutate(state => state.Design.RunAtStartup = achieved, relayout: false);
        });

        body.Children.Add(FluentLayout.Row("", "Start with Windows", "Adds a per-user startup entry", startup));
    }

    private void BuildAbout(Panel body)
    {
        body.Children.Add(FluentLayout.Header("About"));

        var openFolder = new HyperlinkButton { Content = "Open data folder" };
        openFolder.Click += (_, _) =>
        {
            var folder = Path.GetDirectoryName(Themia.Core.Storage.AppStateStore.DefaultPath());
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(folder);
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Themia.Core.Diagnostics.Log.Error("settings", "could not open the data folder", ex);
            }
        };

        body.Children.Add(FluentLayout.Row(
            "",
            "Themia for WinUI",
            "A minimal native rebuild: WinUI 3 on .NET, compiled ahead of time.",
            openFolder));
    }
}
