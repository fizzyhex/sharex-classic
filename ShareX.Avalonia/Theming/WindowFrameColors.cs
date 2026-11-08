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

using Avalonia.Controls;
using Avalonia.Media;
using System.Runtime.InteropServices;

namespace ShareX.AvaloniaUI.Theming
{
    /// <summary>Colors of a native window frame: caption, caption text and border.</summary>
    internal readonly record struct WindowFrameColorSet(Color Caption, Color Text, Color Border);

    /// <summary>
    /// Colors the native title bar and border of a window through DWM (Windows 11). Older versions of Windows ignore
    /// the attributes and keep their own frame.
    /// </summary>
    internal static class WindowFrameColors
    {
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;
        private const uint DWMWA_COLOR_DEFAULT = 0xFFFFFFFF;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

        /// <summary>DWM_WINDOW_CORNER_PREFERENCE values.</summary>
        public const uint CornerDefault = 0;
        public const uint CornerRoundSmall = 3;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(nint windowHandle, int attribute, ref uint value, int size);

        /// <summary>Applies the colors, or restores the system frame when <paramref name="colors"/> is null.</summary>
        public static void Apply(Window window, WindowFrameColorSet? colors)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || window.TryGetPlatformHandle()?.Handle is not nint handle || handle == 0)
            {
                return;
            }

            Set(handle, DWMWA_CAPTION_COLOR, colors?.Caption);
            Set(handle, DWMWA_TEXT_COLOR, colors?.Text);
            Set(handle, DWMWA_BORDER_COLOR, colors?.Border);
        }

        /// <summary>Sets how Windows 11 rounds the corners of the window.</summary>
        public static void SetCornerPreference(Window window, uint preference)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || window.TryGetPlatformHandle()?.Handle is not nint handle || handle == 0)
            {
                return;
            }

            DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(uint));
        }

        private static void Set(nint handle, int attribute, Color? color)
        {
            // COLORREF is 0x00BBGGRR.
            uint value = color is Color c ? (uint)(c.R | (c.G << 8) | (c.B << 16)) : DWMWA_COLOR_DEFAULT;
            DwmSetWindowAttribute(handle, attribute, ref value, sizeof(uint));
        }
    }
}
