using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;

namespace ClaudeAutoPass
{
    /// <summary>
    /// Right-click menu of the tray icon: a small borderless WPF-UI window with the Windows 11 acrylic backdrop (the one
    /// Windows uses for its own menus and flyouts), rounded corners and Fluent icons, light or dark with Windows.
    /// Windows 10 has no such backdrop and gets the plain theme background. Closes when it loses focus or on Esc.
    /// </summary>
    sealed class TrayMenu
    {
        public sealed class Entry
        {
            public string Text;
            public SymbolRegular Icon;
            public bool Enabled = true;
            public bool Bold;
            public Action Click;
            public bool Separator;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Rect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

        [DllImport("user32.dll")]
        static extern bool GetClientRect(IntPtr hWnd, out Rect rect);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        static readonly IntPtr TopMost = new IntPtr(-1);
        const uint NoMove = 0x0002, NoZOrder = 0x0004, NoActivate = 0x0010;

        FluentWindow window;

        /// <summary>The acrylic system backdrop needs Windows 11 22H2 (build 22621); older systems get a solid background.</summary>
        static bool AcrylicSupported => Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22621;

        /// <summary>Creates the WPF application object that holds the WPF-UI theme resources. Call once on the UI thread.</summary>
        public static void InitTheme()
        {
            System.Windows.Application app = System.Windows.Application.Current
                ?? new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
            app.Resources.MergedDictionaries.Add(new ControlsDictionary());
            FollowSystemTheme();
        }

        /// <summary>Matches the menu to the current Windows light/dark setting and accent color.</summary>
        public static void FollowSystemTheme()
        {
            try { ApplicationThemeManager.ApplySystemTheme(true); }
            catch (Exception) { }
        }

        /// <summary>Forces a theme (self-test screenshots).</summary>
        public static void UseTheme(bool dark) => ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.None, true);

        public bool IsOpen => window != null && window.IsVisible;

        /// <summary>Sizes measured when the menu last opened (self-test log and diagnostics export).</summary>
        public string Layout { get; private set; }

        Func<string> fitCheck;
        Action<int> stretch;

        /// <summary>Self-test: null when the open menu's window is exactly as tall as its items, else what differs.</summary>
        public string CheckFit() => IsOpen ? fitCheck?.Invoke() : "menu not open";

        /// <summary>Self-test: makes the open window taller, the way WPF did at 150% scaling, to check it shrinks back.</summary>
        public void StretchForTest(int extraPixels) => stretch?.Invoke(extraPixels);

        /// <summary>Opens the menu next to the given screen point (physical pixels), like a context menu.</summary>
        public void Show(IEnumerable<Entry> entries, System.Drawing.Point at)
        {
            Close();
            // Top-aligned, so the list keeps its natural height even while the window around it is too tall.
            var list = new StackPanel { Margin = new Thickness(0, 4, 0, 4), MinWidth = 248, VerticalAlignment = VerticalAlignment.Top };
            // Display mode snaps glyphs to pixels like native menus; the default "Ideal" mode looks soft for small
            // Chinese text, more so over the acrylic backdrop where ClearType is unavailable.
            TextOptions.SetTextFormattingMode(list, TextFormattingMode.Display);
            TextOptions.SetTextHintingMode(list, TextHintingMode.Fixed);
            var w = new FluentWindow
            {
                ExtendsContentIntoTitleBar = true,
                WindowBackdropType = AcrylicSupported ? WindowBackdropType.Acrylic : WindowBackdropType.None,
                WindowCornerPreference = WindowCornerPreference.Round,
                WindowStartupLocation = WindowStartupLocation.Manual,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                ShowInTaskbar = false,
                ShowActivated = true,
                Topmost = true,
                MinWidth = 0,
                MinHeight = 0,
                Width = double.NaN,
                Height = double.NaN,
                Left = -32000,
                Top = -32000,
                Title = "Claude 自动审批",
                Content = list,
            };
            foreach (Entry e in entries) list.Children.Add(e.Separator ? Separator() : Row(e, w));

            bool closing = false;
            w.Deactivated += delegate { if (!closing) { closing = true; w.Close(); } };
            w.Closing += delegate { closing = true; };
            w.PreviewKeyDown += (s, k) => OnKey(w, list, k);
            w.Closed += delegate { if (window == w) window = null; };
            // The tray app runs a WinForms message loop; this routes keyboard input (Esc) to the WPF window.
            System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(w);
            window = w;
            w.Show();
            // WPF sizes the window as if it still had a caption and frame, although WindowChrome turned the whole
            // window into content; at 150% scaling that left empty space under the last item. Size the native window
            // to the list's natural (desired) size in physical pixels instead, and again if WPF resizes it later.
            w.SizeToContent = SizeToContent.Manual;
            ApplyTheme(w);
            IntPtr handle = new WindowInteropHelper(w).Handle;
            Layout = "";
            int fits = 0;
            void Fit(string reason)
            {
                if (fits++ >= 6 || window != w) return;
                w.UpdateLayout();
                DpiScale dpi = VisualTreeHelper.GetDpi(w);
                list.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Size size = list.DesiredSize; // natural size, including the list's margin
                int width = (int)Math.Ceiling(size.Width * dpi.DpiScaleX), height = (int)Math.Ceiling(size.Height * dpi.DpiScaleY);
                GetWindowRect(handle, out Rect r);
                // The first call also moves the window from its off-screen start to the click point.
                if (reason == "show" || r.Right - r.Left != width || r.Bottom - r.Top != height) Place(handle, at, width, height);
                Layout += reason + ": " + Describe(handle, w, list, dpi) + "\n";
            }
            fitCheck = () =>
            {
                GetWindowRect(handle, out Rect r);
                DpiScale dpi = VisualTreeHelper.GetDpi(w);
                int expected = (int)Math.Ceiling(list.DesiredSize.Height * dpi.DpiScaleY);
                if (r.Bottom - r.Top != expected) return $"window height {r.Bottom - r.Top}px, list {expected}px";
                System.Drawing.Rectangle area = System.Windows.Forms.Screen.FromPoint(at).WorkingArea;
                return area.Contains(new System.Drawing.Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)) ? null
                    : $"window at {r.Left},{r.Top} is outside the work area {area}";
            };
            stretch = extra =>
            {
                GetWindowRect(handle, out Rect r);
                SetWindowPos(handle, IntPtr.Zero, 0, 0, r.Right - r.Left, r.Bottom - r.Top + extra, NoMove | NoZOrder | NoActivate);
            };
            Fit("show");
            w.SizeChanged += delegate { Fit("resized"); };
            w.DpiChanged += delegate { Fit("dpi"); };
            SetForegroundWindow(handle);
            w.Activate();
        }

        /// <summary>Keyboard use like a native menu: arrows move between commands, Enter/Space runs one, Esc closes.</summary>
        static void OnKey(Window w, Panel list, KeyEventArgs k)
        {
            if (k.Key == Key.Escape)
            {
                k.Handled = true;
                w.Close();
                return;
            }
            if (k.Key != Key.Down && k.Key != Key.Up && k.Key != Key.Home && k.Key != Key.End) return;
            k.Handled = true;
            var rows = new List<UIElement>();
            foreach (UIElement child in list.Children) if (child.Focusable && child.IsEnabled) rows.Add(child);
            if (rows.Count == 0) return;
            int current = rows.FindIndex(r => r.IsKeyboardFocused);
            int next = k.Key == Key.Home ? 0 : k.Key == Key.End ? rows.Count - 1
                : current < 0 ? (k.Key == Key.Down ? 0 : rows.Count - 1)
                : (current + (k.Key == Key.Down ? 1 : -1) + rows.Count) % rows.Count;
            Keyboard.Focus(rows[next]);
        }

        static void ApplyTheme(FluentWindow w)
        {
            try
            {
                if (ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark) WindowBackgroundManager.ApplyDarkThemeToWindow(w);
                else WindowBackgroundManager.RemoveDarkThemeFromWindow(w);
            }
            catch (Exception) { }
        }

        /// <summary>Sizes the menu and puts it above and to the right of the point, flipped to stay inside the work area.</summary>
        static void Place(IntPtr handle, System.Drawing.Point at, int width, int height)
        {
            System.Drawing.Rectangle area = System.Windows.Forms.Screen.FromPoint(at).WorkingArea;
            int x = at.X + width <= area.Right ? at.X : at.X - width;
            int y = at.Y - height >= area.Top ? at.Y - height : at.Y;
            x = Math.Max(area.Left, Math.Min(x, area.Right - width));
            y = Math.Max(area.Top, Math.Min(y, area.Bottom - height));
            SetWindowPos(handle, TopMost, x, y, width, height, NoActivate);
        }

        /// <summary>Window, client and content sizes after layout (self-test log and diagnostics export).</summary>
        static string Describe(IntPtr handle, Window w, FrameworkElement list, DpiScale dpi)
        {
            GetWindowRect(handle, out Rect r);
            GetClientRect(handle, out Rect c);
            return $"scale={dpi.DpiScaleX:F2} window={r.Right - r.Left}x{r.Bottom - r.Top}px client={c.Right - c.Left}x{c.Bottom - c.Top}px "
                + $"wpf={w.ActualWidth:F1}x{w.ActualHeight:F1} list={list.DesiredSize.Width:F1}x{list.DesiredSize.Height:F1} (actual {list.ActualWidth:F1}x{list.ActualHeight:F1})";
        }

        static UIElement Separator()
        {
            var line = new Border { Height = 1, Margin = new Thickness(0, 4, 0, 4) };
            line.SetResourceReference(Border.BackgroundProperty, "DividerStrokeColorDefaultBrush");
            return line;
        }

        // Hover and keyboard focus share the highlight; pressing darkens it, like WPF-UI menu items.
        static readonly Lazy<ControlTemplate> RowTemplate = new Lazy<ControlTemplate>(() => (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
            "<ControlTemplate TargetType='Button' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" +
            "<Border x:Name='Bd' Background='Transparent' CornerRadius='4' Margin='4,1,4,1' Padding='12,7,16,7'>" +
            "<ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Border>" +
            "<ControlTemplate.Triggers>" +
            "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bd' Property='Background' Value='{DynamicResource SubtleFillColorSecondaryBrush}'/></Trigger>" +
            "<Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Bd' Property='Background' Value='{DynamicResource SubtleFillColorSecondaryBrush}'/></Trigger>" +
            "<Trigger Property='IsPressed' Value='True'><Setter TargetName='Bd' Property='Background' Value='{DynamicResource SubtleFillColorTertiaryBrush}'/></Trigger>" +
            "<Trigger Property='IsEnabled' Value='False'><Setter TargetName='Bd' Property='Background' Value='Transparent'/></Trigger>" +
            "</ControlTemplate.Triggers></ControlTemplate>"));

        static UIElement Row(Entry e, Window owner)
        {
            var text = new System.Windows.Controls.TextBlock
            {
                Text = e.Text,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = e.Bold ? FontWeights.SemiBold : FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 360,
            };
            var icon = new SymbolIcon { Symbol = e.Icon, FontSize = 16, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            string foreground = e.Enabled ? "TextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush";
            text.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, foreground);
            icon.SetResourceReference(Control.ForegroundProperty, foreground);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (e.Icon != SymbolRegular.Empty) content.Children.Add(icon);
            content.Children.Add(text);
            // A real Button (restyled as a menu row), so the command is focusable, runs on Enter/Space and is
            // exposed to screen readers and UI Automation with its name.
            var row = new System.Windows.Controls.Button
            {
                Content = content,
                // An empty style plus OverridesDefaultStyle keeps WPF-UI's own button look (borders, min size) out.
                Style = new Style(typeof(System.Windows.Controls.Button)),
                OverridesDefaultStyle = true,
                Template = RowTemplate.Value,
                IsEnabled = e.Enabled && e.Click != null,
                FocusVisualStyle = null,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            System.Windows.Automation.AutomationProperties.SetName(row, e.Text);
            if (row.IsEnabled)
            {
                Action click = e.Click;
                row.Click += delegate
                {
                    owner.Close();
                    click();
                };
            }
            return row;
        }

        public void Close()
        {
            Window w = window;
            window = null;
            try { w?.Close(); }
            catch (InvalidOperationException) { }
        }
    }
}
