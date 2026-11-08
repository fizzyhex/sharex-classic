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

using Avalonia.Animation.Easings;

namespace ShareX.AvaloniaUI.Theming
{
    /// <summary>
    /// A sine ease in and out whose output only takes <see cref="Steps"/> distinct values. The Aero theme uses it for
    /// its slow ambient animations (the start screen banner and the default button pulse): the animation clock still
    /// ticks every frame, but the animated value, and so the window, only changes a few times a second. An unchanged
    /// value invalidates nothing, so nothing is drawn between steps.
    /// </summary>
    public sealed class AeroStepEasing : Easing
    {
        /// <summary>The number of distinct values over the whole animation.</summary>
        public int Steps { get; set; } = 24;

        public override double Ease(double progress)
        {
            double eased = (1 - Math.Cos(Math.PI * progress)) / 2;
            int steps = Math.Max(1, Steps);
            return Math.Round(eased * steps) / steps;
        }
    }
}
