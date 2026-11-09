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

using ShareX.HelpersLib;

namespace ShareX
{
    internal class ShareXUpdateManager : GitHubUpdateManager
    {
        public UpdateChannel UpdateChannel { get; set; }

        // ShareX Classic updates from its own releases so upstream setups don't replace it
        public override GitHubUpdateChecker CreateUpdateChecker()
        {
            return new ClassicUpdateChecker()
            {
                IsPortable = StartupOptions.Portable,
                IncludePreRelease = UpdateChannel != UpdateChannel.Release
            };
        }
    }

    internal class ClassicUpdateChecker : GitHubUpdateChecker
    {
        private const string TagSuffix = "-classic.";

        public ClassicUpdateChecker() : base("fizzyhex", "sharex-classic")
        {
        }

        // Tags look like "v22.4.0-classic.3", which maps to version 22.4.0.3
        protected override bool UpdateReleaseInfo(GitHubRelease release, bool isPortable, bool isBrowserDownloadURL)
        {
            if (release?.tag_name != null)
            {
                int index = release.tag_name.IndexOf(TagSuffix);

                if (index > 0)
                {
                    release.tag_name = $"{release.tag_name.Substring(0, index)}.{release.tag_name.Substring(index + TagSuffix.Length)}";
                }
            }

            return base.UpdateReleaseInfo(release, isPortable, isBrowserDownloadURL);
        }
    }
}