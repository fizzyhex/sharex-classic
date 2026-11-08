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
using Avalonia.Controls.Chrome;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Runtime.CompilerServices;

namespace ShareX.AvaloniaUI.Theming
{
    /// <summary>
    /// Gives windows with a standard frame the Windows 7 Aero frame while an Aero theme is active. The window only gets
    /// classes here; AeroTheme.axaml turns them into the extended client area, the drawn decorations (glass, caption and
    /// caption buttons), the transparency request and the client area inset, so removing the classes or the theme
    /// styles restores the window as it was.
    /// </summary>
    internal static class AeroWindowFrame
    {
        private const string FrameClass = "aero-frame";
        private const string GlassClass = "aero-glass";
        private const string MaximizedClass = "aero-maximized";
        private const string ActiveClass = "aero-active";
        private const string InactiveDecorationsClass = "inactive";
        private const string OpaqueDecorationsClass = "opaque";

        private sealed class State
        {
            public bool Glass;
            public WindowIcon? Icon;
            public Bitmap? IconBitmap;
        }

        private static readonly ConditionalWeakTable<Window, State> States = new();

        public static void Apply(Window window, bool enabled, bool glass)
        {
            bool framed = window.Classes.Contains(FrameClass);

            // Only windows with the standard frame are redrawn: borderless, tool and overlay windows keep their look. A
            // window that already has the frame requests transparency through the Aero styles, so it stays eligible.
            bool eligible = enabled && window.WindowDecorations == WindowDecorations.Full &&
                (framed || !window.TransparencyLevelHint.Contains(WindowTransparencyLevel.Transparent));

            if (!eligible)
            {
                if (framed)
                {
                    window.Classes.Remove(FrameClass);
                    window.Classes.Remove(GlassClass);
                    window.Classes.Remove(MaximizedClass);
                    window.Classes.Remove(ActiveClass);
                    WindowFrameColors.SetCornerPreference(window, WindowFrameColors.CornerDefault);
                }

                return;
            }

            if (!States.TryGetValue(window, out State? state))
            {
                state = new State();
                States.Add(window, state);
                window.Opened += (_, _) => Update(window);
                window.Activated += (_, _) => Update(window);
                window.Deactivated += (_, _) => Update(window);
                window.PropertyChanged += (_, e) =>
                {
                    if (e.Property == Window.WindowStateProperty || e.Property == TopLevel.ActualTransparencyLevelProperty ||
                        e.Property == Window.IconProperty)
                    {
                        Update(window);
                    }
                };
            }

            state.Glass = glass;
            window.Classes.Set(FrameClass, true);
            window.Classes.Set(GlassClass, glass);
            Update(window);

            // The decorations are created when the styles extend the client area, which can happen after this returns.
            Dispatcher.UIThread.Post(() => Update(window), DispatcherPriority.Background);
        }

        private static void Update(Window window)
        {
            if (!window.Classes.Contains(FrameClass) || !States.TryGetValue(window, out State? state))
            {
                return;
            }

            bool active = window.IsActive;
            window.Classes.Set(MaximizedClass, window.WindowState == WindowState.Maximized);
            window.Classes.Set(ActiveClass, active);

            // Aero Basic, and Aero while the system offers no blur (transparency effects off, battery saver), draw the
            // opaque frame.
            bool opaque = !state.Glass || window.ActualTransparencyLevel == WindowTransparencyLevel.None;

            if (window.GetVisualParent() is Visual host)
            {
                if (((ILogical)host).LogicalChildren.OfType<WindowDrawnDecorations>().FirstOrDefault() is WindowDrawnDecorations decorations)
                {
                    decorations.Classes.Set(InactiveDecorationsClass, !active);
                    decorations.Classes.Set(OpaqueDecorationsClass, opaque);
                }

                UpdateCaptionIcon(window, host, state);
            }

            // Windows 7 frames have small rounded corners rather than the large Windows 11 ones.
            WindowFrameColors.SetCornerPreference(window, WindowFrameColors.CornerRoundSmall);
        }

        private static void UpdateCaptionIcon(Window window, Visual host, State state)
        {
            if (!ReferenceEquals(state.Icon, window.Icon))
            {
                state.Icon = window.Icon;
                state.IconBitmap = null;

                if (window.Icon != null)
                {
                    try
                    {
                        using MemoryStream stream = new();
                        window.Icon.Save(stream);
                        stream.Position = 0;
                        state.IconBitmap = new Bitmap(stream);
                    }
                    catch (Exception)
                    {
                        // The caption shows no icon if the window icon cannot be read back.
                        state.IconBitmap = null;
                    }
                }
            }

            // The decorations live beside the window in its host, not inside it.
            foreach (Visual layer in host.GetVisualChildren())
            {
                if (layer is TopLevel)
                {
                    continue;
                }

                foreach (Visual visual in layer.GetVisualDescendants())
                {
                    if (visual is Image { Name: "AeroCaptionIcon" } image)
                    {
                        if (!ReferenceEquals(image.Source, state.IconBitmap))
                        {
                            image.Source = state.IconBitmap;
                        }

                        return;
                    }
                }
            }
        }
    }
}
