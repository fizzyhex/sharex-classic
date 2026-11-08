#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using ShareX.Icons;
using Avalonia.Controls.Primitives;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ShareX.AvaloniaUI.Theming
{
    public static class ThemeManager
    {
        public static readonly ThemeVariant ShareXDark = new ThemeVariant("ShareXDark", ThemeVariant.Dark);
        public static readonly ThemeVariant ShareXLight = new ThemeVariant("ShareXLight", ThemeVariant.Light);
        // The classic themes recreate the Windows Forms look of ShareX 21 and earlier: Classic Light is the
        // native Windows look, Classic Dark is the dark theme ShareX 21 applied to the same controls.
        public static readonly ThemeVariant ShareXClassicLight = new ThemeVariant("ShareXClassicLight", ThemeVariant.Light);
        public static readonly ThemeVariant ShareXClassicDark = new ThemeVariant("ShareXClassicDark", ThemeVariant.Dark);

        // Windows Forms draws selection with the system highlight color (#0078D7). The classic themes take the
        // accent color as the highlight instead and tint the rest of the palette with its hue; a white accent
        // keeps the standard Windows colors. Links stay the fixed system link colors.
        private static readonly Color ClassicHighlightColor = Color.Parse("#0078D7");
        private static readonly HslColor ClassicHighlightHsl = ClassicHighlightColor.ToHsl();
        private const double ClassicTintStrength = 0.4;
        private const double ClassicTintMaximumChroma = 0.12;
        private const double ClassicThumbTintFactor = 2.5;
        private static readonly ResourceDictionary ClassicTintResources = new();
        private static (ThemeVariant Theme, Color Accent)? _classicTint;
        // A tinted classic theme colors the native title bar like Windows does with "Show accent color on title bars":
        // the active window's caption is the accent, inactive captions fall back to the tinted window color.
        private static WindowFrameColorSet? _activeFrame;
        private static WindowFrameColorSet? _inactiveFrame;
        private static readonly ConditionalWeakTable<Window, object> FrameTrackedWindows = new();
        private static readonly Color ClassicLightLinkColor = Color.Parse("#0066CC");
        private static readonly Color ClassicDarkLinkColor = Color.Parse("#A6D4FF");
        private static ThemeVariant _currentTheme = ShareXDark;
        private static ApplicationThemeOptions? _options;
        private static IPlatformSettings? _platformSettings;
        private static ResourceInclude? _compactResources;
        private static StyleInclude? _classicStyles;
        private static readonly ConditionalWeakTable<Window, StyleInclude> WindowClassicStyles = new();
        private static bool _windowClassicHandlerRegistered;
        private static readonly ResourceDictionary SpacingResources = new();

        public static event EventHandler<ThemeVariant>? ThemeChanged;
        public static event EventHandler<Color>? AccentColorChanged;
        public static event EventHandler? SpacingChanged;

        public static void Configure(ApplicationThemeOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (!ReferenceEquals(_options, options))
            {
                if (_options != null)
                {
                    _options.PropertyChanged -= OnOptionsPropertyChanged;
                }

                _options = options;
                _options.PropertyChanged += OnOptionsPropertyChanged;
            }

            RunOnUIThread(ApplyConfiguredSettings);
        }

        public static void SetTheme(ThemeVariant theme, object? target = null)
        {
            bool themeChanged = !Equals(_currentTheme, theme);
            _currentTheme = theme;

            if (target is Application app)
            {
                ApplyClassicStyles(app, IsClassicVariant(theme));
                app.RequestedThemeVariant = theme;

                if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    foreach (Window window in desktop.Windows)
                    {
                        window.RequestedThemeVariant = theme;
                    }
                }
            }
            else if (target is Window window)
            {
                window.RequestedThemeVariant = theme;
            }
            else if (target is ThemeVariantScope scope)
            {
                scope.RequestedThemeVariant = theme;
            }

            if (themeChanged)
            {
                ThemeChanged?.Invoke(null, theme);
            }
        }

        public static ThemeVariant GetCurrentTheme()
        {
            return _currentTheme;
        }

        public static bool IsDarkTheme => Equals(_currentTheme, ShareXDark) || Equals(_currentTheme, ShareXClassicDark);

        public static bool IsClassicTheme => IsClassicVariant(_currentTheme);

        public static void Refresh()
        {
            RunOnUIThread(ApplyConfiguredSettings);
        }

        private static void OnOptionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Spacing changes arrive continuously while a slider is dragged, so they skip the theme and
            // accent refresh, which replaces brushes across the whole application.
            if (ApplicationThemeOptions.IsSpacingProperty(e.PropertyName))
            {
                RunOnUIThread(() => ApplySpacing(Application.Current));
                return;
            }

            RunOnUIThread(ApplyConfiguredSettings);
        }

        private static void ApplyConfiguredSettings()
        {
            if (_options == null)
            {
                return;
            }

            Application? application = Application.Current;
            SetPlatformSettings((_options.UseSystemTheme || _options.UseSystemAccentColor)
                ? application?.PlatformSettings
                : null);

            PlatformColorValues? colorValues = _platformSettings?.GetColorValues();
            ThemeVariant theme = _options.UseSystemTheme
                ? MapSystemTheme(colorValues, _options.Theme)
                : MapConfiguredTheme(_options.Theme);

            SetTheme(theme, application);

            Color accentColor = _options.AccentColor;
            if (_options.UseSystemAccentColor && colorValues is { AccentColor1.A: > 0 })
            {
                accentColor = colorValues.AccentColor1;
            }

            ApplyAccentColor(application, accentColor, theme);
            IconCatalog.Pack = _options.IconPack;
            ApplySpacing(application);
        }

        private static void ApplyClassicStyles(Application application, bool enabled)
        {
            // The classic control themes replace the Fluent templates, so they are only part of the application
            // styles while the classic theme is active. They come last so they win over ControlStyles.axaml.
            _classicStyles ??= new StyleInclude(new Uri("avares://ShareX.Avalonia/"))
            {
                Source = new Uri("avares://ShareX.Avalonia/Theming/ClassicTheme.axaml")
            };

            bool included = application.Styles.Contains(_classicStyles);

            if (enabled && !included)
            {
                application.Styles.Add(_classicStyles);
            }
            else if (!enabled && included)
            {
                application.Styles.Remove(_classicStyles);
            }

            if (enabled != included && application.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
            {
                foreach (Window window in lifetime.Windows)
                {
                    ReapplyStyling(window);
                }
            }

            // Window styles are applied after application styles, and most windows restyle buttons, cards and
            // inputs, so the classic styles are also appended to each window. New windows get them as soon as
            // their template is applied, before the first frame is drawn.
            if (!_windowClassicHandlerRegistered)
            {

                TemplatedControl.TemplateAppliedEvent.AddClassHandler<Window>((window, _) => ApplyClassicStyles(window, IsClassicTheme));
                _windowClassicHandlerRegistered = true;
            }

            if (application.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (Window window in desktop.Windows)
                {
                    ApplyClassicStyles(window, enabled);
                }
            }
        }

        private static void ApplyClassicStyles(Window window, bool enabled)
        {
            ApplyWindowFrame(window);

            if (enabled)
            {
                StyleInclude styles = WindowClassicStyles.GetValue(window, _ => new StyleInclude(new Uri("avares://ShareX.Avalonia/"))
                {
                    Source = new Uri("avares://ShareX.Avalonia/Theming/ClassicTheme.axaml")
                });

                // Keep the classic styles last, after any styles the window added since.
                if (window.Styles.Count == 0 || !ReferenceEquals(window.Styles[^1], styles))
                {
                    window.Styles.Remove(styles);
                    window.Styles.Add(styles);
                    ReapplyStyling(window);
                }
            }
            else if (WindowClassicStyles.TryGetValue(window, out StyleInclude? styles) && window.Styles.Remove(styles))
            {
                ReapplyStyling(window);
            }
        }

        private static void ReapplyStyling(Window window)
        {
            // Changing a styles collection removes the style values (including Theme, and so Template) from every
            // control at once but applies the new ones lazily. A layout pass in between applies an empty template,
            // which controls with required template parts such as ToggleSwitch reject, so restyle straight away.
            // Early in a window's life its content is only a logical child, so both trees are walked.
            window.ApplyStyling();

            foreach (ILogical logical in window.GetLogicalDescendants())
            {
                (logical as StyledElement)?.ApplyStyling();
            }

            foreach (Visual visual in window.GetVisualDescendants())
            {
                visual.ApplyStyling();
            }
        }

        private static void ApplySpacing(Application? application)
        {
            if (application == null || _options == null)
            {
                return;
            }

            // Application resources are looked up before application styles, so these values
            // override the default spacing declared in ControlStyles.axaml.
            IList<IResourceProvider> dictionaries = application.Resources.MergedDictionaries;
            if (!dictionaries.Contains(SpacingResources))
            {
                dictionaries.Add(SpacingResources);
            }

            _compactResources ??= new ResourceInclude(new Uri("avares://ShareX.Avalonia/"))
            {
                Source = new Uri("avares://ShareX.Avalonia/Theming/CompactMode.axaml")
            };

            bool changed = false;

            // CompactMode.axaml lists every spacing value with its compact size; the default size of the same
            // key comes from the application styles, and the configured spacing picks a point between the two.
            foreach (object key in _compactResources.Loaded.Keys.ToArray())
            {
                if (key is not string name || !TryGetSpacingArea(name, out SpacingArea area) ||
                    !_compactResources.TryGetResource(key, _currentTheme, out object? compactValue) ||
                    !application.Styles.TryGetResource(key, _currentTheme, out object? defaultValue))
                {
                    continue;
                }

                double amount = _options.GetSpacing(area) / (double)ApplicationThemeOptions.DefaultSpacing;

                // Wider spacing doesn't need a wider settings window, so the window width only shrinks.
                if (name == "ShareX.Density.Settings.WindowWidth")
                {
                    amount = Math.Min(amount, 1);
                }

                object? value = InterpolateSpacing(name, compactValue, defaultValue, amount);

                if (value != null && (!SpacingResources.TryGetValue(key, out object? current) || !Equals(current, value)))
                {
                    SpacingResources[key] = value;
                    changed = true;
                }
            }

            if (changed)
            {
                SpacingChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        private static bool TryGetSpacingArea(string key, out SpacingArea area)
        {
            if (key.StartsWith("ShareX.Density.Nav.", StringComparison.Ordinal))
            {
                area = SpacingArea.Sidebar;
            }
            else if (key.StartsWith("ShareX.Density.Menu.", StringComparison.Ordinal) ||
                key is "MenuFlyoutPresenterThemePadding" or "MenuIconPresenterMargin")
            {
                area = SpacingArea.Menus;
            }
            else if (key.StartsWith("ShareX.Density.ToolGrid.", StringComparison.Ordinal))
            {
                area = SpacingArea.ToolMenus;
            }
            else if (key.StartsWith("ShareX.Density.Settings.", StringComparison.Ordinal))
            {
                area = SpacingArea.SettingsWindows;
            }
            else
            {
                area = default;
                return false;
            }

            return true;
        }

        private static object? InterpolateSpacing(string key, object? compactValue, object? defaultValue, double amount)
        {
            // Text sizes move in half-pixel steps so they stay readable; everything else snaps to whole pixels.
            bool isTextSize = key.EndsWith("FontSize", StringComparison.Ordinal) || key.EndsWith("IconSize", StringComparison.Ordinal);

            double Lerp(double compact, double comfortable)
            {
                double value = Math.Max(0, compact + ((comfortable - compact) * amount));
                return isTextSize ? Math.Round(value * 2) / 2 : Math.Round(value);
            }

            return (compactValue, defaultValue) switch
            {
                (double compact, double comfortable) => Lerp(compact, comfortable),
                (Thickness compact, Thickness comfortable) => new Thickness(
                    Lerp(compact.Left, comfortable.Left),
                    Lerp(compact.Top, comfortable.Top),
                    Lerp(compact.Right, comfortable.Right),
                    Lerp(compact.Bottom, comfortable.Bottom)),
                _ => null
            };
        }

        private static void SetPlatformSettings(IPlatformSettings? platformSettings)
        {
            if (ReferenceEquals(_platformSettings, platformSettings))
            {
                return;
            }

            if (_platformSettings != null)
            {
                _platformSettings.ColorValuesChanged -= OnPlatformColorValuesChanged;
            }

            _platformSettings = platformSettings;

            if (_platformSettings != null)
            {
                _platformSettings.ColorValuesChanged += OnPlatformColorValuesChanged;
            }
        }

        private static void OnPlatformColorValuesChanged(object? sender, PlatformColorValues e)
        {
            RunOnUIThread(ApplyConfiguredSettings);
        }

        private static ThemeVariant MapSystemTheme(PlatformColorValues? colorValues, string configuredTheme)
        {
            if (colorValues != null)
            {
                return IsLightTheme(colorValues.ThemeVariant.ToString()) ? ShareXLight : ShareXDark;
            }

            return MapConfiguredTheme(configuredTheme);
        }

        private static ThemeVariant MapConfiguredTheme(string? theme)
        {
            if (IsClassicThemeName(theme))
            {
                return theme!.Contains("Dark", StringComparison.OrdinalIgnoreCase) ? ShareXClassicDark : ShareXClassicLight;
            }

            return IsLightTheme(theme) ? ShareXLight : ShareXDark;
        }

        private static bool IsClassicVariant(ThemeVariant theme)
        {
            return Equals(theme, ShareXClassicLight) || Equals(theme, ShareXClassicDark);
        }

        public static bool IsClassicThemeName(string? theme)
        {
            return !string.IsNullOrWhiteSpace(theme) &&
                theme.Contains("Classic", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLightTheme(string? theme)
        {
            return !string.IsNullOrWhiteSpace(theme) &&
                theme.Contains("Light", StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyAccentColor(Application? application, Color startColor, ThemeVariant theme)
        {
            if (application == null)
            {
                return;
            }

            bool classic = IsClassicVariant(theme);
            bool tinted = classic && !IsClassicNeutralAccent(startColor);
            if (classic)
            {
                startColor = tinted ? Color.FromRgb(startColor.R, startColor.G, startColor.B) : ClassicHighlightColor;
            }

            ApplyClassicTint(application, theme, tinted ? startColor : null);

            Color endColor = classic ? startColor : DarkenColor(startColor, 0.10);
            Color foregroundColor = classic ? GetClassicHighlightTextColor(startColor) : GetAccentForegroundColor(startColor, endColor);
            UpdateWindowFrames(application, theme, tinted ? startColor : null, foregroundColor);
            Color linkColor = !classic ? GetLinkColor(application, startColor, theme)
                : Equals(theme, ShareXClassicDark) ? ClassicDarkLinkColor : ClassicLightLinkColor;

            // Links are drawn on the window background rather than on the accent, so a dark or grey
            // accent would make them unreadable on the dark theme (and a pale one on the light theme).
            application.Resources["ShareX.Color.Link"] = linkColor;
            application.Resources["ShareX.Brush.Link"] = new SolidColorBrush(linkColor);
            application.Resources["HyperlinkButtonForeground"] = new SolidColorBrush(linkColor);
            application.Resources["HyperlinkButtonForegroundPointerOver"] = new SolidColorBrush(linkColor);
            application.Resources["HyperlinkButtonForegroundPressed"] = new SolidColorBrush(linkColor);

            // Selected rows in lists and trees. The classic accent is the system highlight, which the Windows Forms
            // tree view fills the selected row with, so both cases come from the accent colors.
            application.Resources["ShareX.Brush.Selection.Background"] = CreateAccentBrush(startColor, endColor);
            application.Resources["ShareX.Brush.Selection.Border"] = new SolidColorBrush(startColor);
            application.Resources["ShareX.Brush.Selection.Foreground"] = new SolidColorBrush(foregroundColor);

            application.Resources["SystemAccentColor"] = startColor;
            application.Resources["ShareX.Color.Accent.Start"] = startColor;
            application.Resources["ShareX.Color.Accent.End"] = endColor;
            application.Resources["ShareX.Color.Accent.Foreground"] = foregroundColor;
            application.Resources["ShareX.Brush.Accent.Start"] = new SolidColorBrush(startColor);
            application.Resources["ShareX.Brush.Accent.Foreground"] = new SolidColorBrush(foregroundColor);
            application.Resources["ShareX.Brush.Accent"] = CreateAccentBrush(startColor, endColor);

            AccentColorChanged?.Invoke(null, startColor);
        }

        /// <summary>
        /// White (or a near-white grey such as the palette's white swatch) asks the classic themes for the standard
        /// Windows colors rather than a tint.
        /// </summary>
        public static bool IsClassicNeutralAccent(Color color)
        {
            byte min = Math.Min(color.R, Math.Min(color.G, color.B));
            byte max = Math.Max(color.R, Math.Max(color.G, color.B));
            return min >= 0xE6 && max - min <= 0x10;
        }

        private static Color GetClassicHighlightTextColor(Color highlight)
        {
            // Windows keeps white highlight text; a pale accent needs black to stay readable.
            return GetContrastRatio(Colors.White, highlight) >= 3 ? Colors.White : Colors.Black;
        }

        private static void ApplyClassicTint(Application application, ThemeVariant theme, Color? accent)
        {
            IList<IResourceProvider> dictionaries = application.Resources.MergedDictionaries;
            if (!dictionaries.Contains(ClassicTintResources))
            {
                dictionaries.Add(ClassicTintResources);
            }

            (ThemeVariant, Color)? tint = accent is Color color ? (theme, color) : null;
            if (Equals(_classicTint, tint))
            {
                return;
            }

            _classicTint = tint;

            // Merged dictionaries are searched last first, so these values override the classic theme dictionary
            // they are computed from. Dropping them restores it.
            ClassicTintResources.Clear();

            if (accent is not Color accentColor || !TryGetThemeDictionary(application, theme, out ResourceDictionary? source))
            {
                return;
            }

            HslColor accentHsl = accentColor.ToHsl();
            Dictionary<object, object?> values = new();

            foreach (object key in source.Keys)
            {
                if (key is not string name || !IsTintableResource(name) || !source.TryGetResource(key, theme, out object? value))
                {
                    continue;
                }

                // Scroll bar thumbs are small light or dark greys where a surface tint would hardly show, so they take
                // a stronger one and read as accent-colored grips.
                double strength = name.StartsWith("Classic.ScrollBar.Thumb", StringComparison.Ordinal) ? ClassicThumbTintFactor : 1;
                object? tinted = value switch
                {
                    Color c => TintClassicColor(c, accentHsl, strength),
                    ISolidColorBrush brush => new SolidColorBrush(TintClassicColor(brush.Color, accentHsl, strength), brush.Opacity),
                    LinearGradientBrush gradient => TintGradient(gradient, accentHsl),
                    _ => null
                };

                if (tinted != null)
                {
                    values[key] = tinted;
                }
            }

            Color highlightText = GetClassicHighlightTextColor(accentColor);
            values["Classic.Highlight"] = new SolidColorBrush(accentColor);
            values["Classic.HighlightText"] = new SolidColorBrush(highlightText);
            values["Classic.Tree.Selected"] = new SolidColorBrush(accentColor);
            values["Classic.Tree.Selected.Text"] = new SolidColorBrush(highlightText);

            foreach (KeyValuePair<object, object?> pair in values)
            {
                ClassicTintResources[pair.Key] = pair.Value;
            }
        }

        private static void UpdateWindowFrames(Application application, ThemeVariant theme, Color? accent, Color accentText)
        {
            if (accent is Color accentColor)
            {
                _activeFrame = new WindowFrameColorSet(accentColor, accentText, accentColor);
                _inactiveFrame = new WindowFrameColorSet(
                    GetThemeColor(application, theme, "ShareX.Color.Background.Main", accentColor),
                    GetThemeColor(application, theme, "Classic.GrayText", accentText),
                    GetThemeColor(application, theme, "ShareX.Color.Border", accentColor));
            }
            else
            {
                _activeFrame = null;
                _inactiveFrame = null;
            }

            if (application.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (Window window in desktop.Windows)
                {
                    ApplyWindowFrame(window);
                }
            }
        }

        private static void ApplyWindowFrame(Window window)
        {
            if (!FrameTrackedWindows.TryGetValue(window, out _))
            {
                FrameTrackedWindows.Add(window, new object());
                window.Opened += (_, _) => ApplyWindowFrame(window);
                window.Activated += (_, _) => ApplyWindowFrame(window);
                window.Deactivated += (_, _) => ApplyWindowFrame(window);
            }

            WindowFrameColors.Apply(window, window.IsActive ? _activeFrame : _inactiveFrame);
        }

        private static Color GetThemeColor(Application application, ThemeVariant theme, string key, Color fallback)
        {
            return application.TryGetResource(key, theme, out object? value)
                ? value switch
                {
                    Color color => color,
                    ISolidColorBrush brush => brush.Color,
                    _ => fallback
                }
                : fallback;
        }

        private static bool TryGetThemeDictionary(Application application, ThemeVariant theme, [NotNullWhen(true)] out ResourceDictionary? dictionary)
        {
            foreach (IResourceProvider provider in application.Resources.MergedDictionaries)
            {
                ResourceDictionary? candidate = provider switch
                {
                    ResourceInclude include => include.Loaded as ResourceDictionary,
                    ResourceDictionary resources => resources,
                    _ => null
                };

                if (candidate?.ThemeDictionaries.TryGetValue(theme, out IThemeVariantProvider? variant) == true &&
                    variant is ResourceDictionary variantResources)
                {
                    dictionary = variantResources;
                    return true;
                }
            }

            dictionary = null;
            return false;
        }

        private static bool IsTintableResource(string key)
        {
            if (!key.StartsWith("Classic.", StringComparison.Ordinal) && !key.StartsWith("ShareX.Color.", StringComparison.Ordinal) &&
                !key.StartsWith("ShareX.Brush.", StringComparison.Ordinal) && key != "MenuFlyoutPresenterBackground" &&
                key != "ColorViewTabBackgroundBrush")
            {
                return false;
            }

            // Text stays black or white, and colors with a meaning of their own (status, the green progress fill,
            // the red close button, links) keep it. The accent and highlight entries are set from the accent itself.
            string[] fixedParts = ["Text", "Accent", "Link", "Status", "TaskMode", "CaptionClose", "Progress.Fill", "Overlay", "Checkered"];
            return !fixedParts.Any(part => key.Contains(part, StringComparison.Ordinal));
        }

        private static LinearGradientBrush TintGradient(LinearGradientBrush gradient, HslColor accent)
        {
            LinearGradientBrush tinted = new()
            {
                StartPoint = gradient.StartPoint,
                EndPoint = gradient.EndPoint,
                Opacity = gradient.Opacity
            };

            foreach (GradientStop stop in gradient.GradientStops)
            {
                tinted.GradientStops.Add(new GradientStop(TintClassicColor(stop.Color, accent), stop.Offset));
            }

            return tinted;
        }

        private static Color TintClassicColor(Color color, HslColor accent, double strength = 1)
        {
            if (color.A == 0)
            {
                return color;
            }

            HslColor hsl = color.ToHsl();
            double hueDistance = Math.Abs(hsl.H - ClassicHighlightHsl.H);
            hueDistance = Math.Min(hueDistance, 360 - hueDistance);

            if (hsl.S >= 0.25 && hueDistance <= 25)
            {
                // The Windows blues (highlight, hot and pressed button faces and borders, menu selection) are
                // lighter or darker steps of the highlight color, so they become the same steps of the accent.
                double reference = ClassicHighlightHsl.L;
                double lightness = hsl.L >= reference
                    ? accent.L + ((hsl.L - reference) * (1 - accent.L) / (1 - reference))
                    : hsl.L * accent.L / reference;
                double saturation = Math.Min(1, hsl.S * accent.S / ClassicHighlightHsl.S);
                return new HslColor(color.A / 255d, accent.H, saturation, lightness).ToRgb();
            }

            // Greys take on the accent hue. The chroma is capped so mid greys such as borders stay a tint.
            double range = 1 - Math.Abs((2 * hsl.L) - 1);
            if (range <= 0)
            {
                return color;
            }

            double chroma = Math.Min(ClassicTintStrength * strength * accent.S * range, ClassicTintMaximumChroma * strength);
            return new HslColor(color.A / 255d, accent.H, chroma / range, hsl.L).ToRgb();
        }

        private static IBrush CreateAccentBrush(Color startColor, Color endColor)
        {
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(startColor, 0),
                    new GradientStop(endColor, 1)
                }
            };
        }

        private static Color DarkenColor(Color color, double amount)
        {
            double factor = Math.Clamp(1 - amount, 0, 1);
            return Color.FromArgb(color.A,
                (byte)Math.Round(color.R * factor),
                (byte)Math.Round(color.G * factor),
                (byte)Math.Round(color.B * factor));
        }

        private static Color GetAccentForegroundColor(Color startColor, Color endColor)
        {
            Color light = Color.Parse("#D8DADB");
            Color dark = Color.Parse("#4E4E4E");
            double lightContrast = Math.Min(GetContrastRatio(light, startColor), GetContrastRatio(light, endColor));
            double darkContrast = Math.Min(GetContrastRatio(dark, startColor), GetContrastRatio(dark, endColor));
            return darkContrast >= lightContrast * 1.75 ? dark : light;
        }

        private static Color GetLinkColor(Application application, Color accentColor, ThemeVariant theme)
        {
            const double minimumContrast = 4.5;
            const int steps = 20;

            Color background = application.TryGetResource("ShareX.Color.Background.Main", theme, out object? value) && value is Color color
                ? color
                : IsLightTheme(theme.ToString()) ? Colors.White : Colors.Black;
            Color target = GetRelativeLuminance(background) < 0.5 ? Colors.White : Colors.Black;
            Color opaqueAccent = Color.FromRgb(accentColor.R, accentColor.G, accentColor.B);

            for (int step = 0; step <= steps; step++)
            {
                Color candidate = BlendColor(opaqueAccent, target, step / (double)steps);

                if (GetContrastRatio(candidate, background) >= minimumContrast)
                {
                    return candidate;
                }
            }

            return target;
        }

        private static Color BlendColor(Color from, Color to, double amount)
        {
            return Color.FromRgb(
                (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
                (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
                (byte)Math.Round(from.B + ((to.B - from.B) * amount)));
        }

        private static double GetContrastRatio(Color first, Color second)
        {
            double firstLuminance = GetRelativeLuminance(first);
            double secondLuminance = GetRelativeLuminance(second);
            return (Math.Max(firstLuminance, secondLuminance) + 0.05) /
                (Math.Min(firstLuminance, secondLuminance) + 0.05);
        }

        private static double GetRelativeLuminance(Color color)
        {
            static double Linearize(byte channel)
            {
                double value = channel / 255d;
                return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Linearize(color.R)) +
                (0.7152 * Linearize(color.G)) +
                (0.0722 * Linearize(color.B));
        }

        private static void RunOnUIThread(Action action)
        {
            if (Application.Current == null)
            {
                throw new InvalidOperationException("Avalonia must be initialized before applying theme changes.");
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
            }
            else
            {
                Dispatcher.UIThread.Post(action);
            }
        }
    }
}
