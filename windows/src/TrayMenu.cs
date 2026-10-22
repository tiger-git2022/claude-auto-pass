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
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        static readonly IntPtr TopMost = new IntPtr(-1);
        const uint NoSize = 0x0001, NoActivate = 0x0010;

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

        /// <summary>Opens the menu next to the given screen point (physical pixels), like a context menu.</summary>
        public void Show(IEnumerable<Entry> entries, System.Drawing.Point at)
        {
            Close();
            var list = new StackPanel { Margin = new Thickness(0, 4, 0, 4), MinWidth = 248 };
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
            ApplyTheme(w);
            Place(w, at);
            IntPtr handle = new WindowInteropHelper(w).Handle;
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

        /// <summary>Puts the menu above and to the right of the point, flipped to stay inside the work area.</summary>
        static void Place(Window w, System.Drawing.Point at)
        {
            IntPtr handle = new WindowInteropHelper(w).Handle;
            if (!GetWindowRect(handle, out Rect r)) return;
            int width = r.Right - r.Left, height = r.Bottom - r.Top;
            System.Drawing.Rectangle area = System.Windows.Forms.Screen.FromPoint(at).WorkingArea;
            int x = at.X + width <= area.Right ? at.X : at.X - width;
            int y = at.Y - height >= area.Top ? at.Y - height : at.Y;
            x = Math.Max(area.Left, Math.Min(x, area.Right - width));
            y = Math.Max(area.Top, Math.Min(y, area.Bottom - height));
            SetWindowPos(handle, TopMost, x, y, 0, 0, NoSize | NoActivate);
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
