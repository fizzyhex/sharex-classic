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
using SkiaSharp;
using System.Drawing;

namespace ShareX.ScreenRecordingLib.Encoding;

internal sealed partial class GifWriter
{
    public static VideoEncoderInfo WebPEncoder { get; } = new("ShareX animated WebP encoder", false, false);
    // Lossless WebP effort; higher values encode much more slowly for little gain on screen content.
    private const int WebPLosslessEffort = 50;
    private sealed record WebPFrame(Task<byte[]> Data, Rectangle Bounds)
    {
        public long Until { get; set; }
    }

    private void FinishWebP(long duration, Action<int> progress, Func<bool> aborted)
    {
        if (imageInfo.Width > AnimatedWebPCreator.MaxDimension || imageInfo.Height > AnimatedWebPCreator.MaxDimension)
            throw new NotSupportedException($"Animated WebP frames cannot exceed {AnimatedWebPCreator.MaxDimension} pixels per side.");
        int previousProgress = 0;
        void ReportProgress(int percent)
        {
            if (percent > previousProgress) { progress(percent); previousProgress = percent; }
        }
        // Size fallback: try full quality first and switch to the lossy quality only if the
        // file outgrows the limit. The lossless pass stops as soon as it crosses the limit.
        bool sizeFallback = webPLossyAboveBytes > 0 && webPQuality < 100;
        if (sizeFallback)
        {
            if (EncodeWebPPass(100, webPLossyAboveBytes, duration, percent => ReportProgress(percent), aborted)) { ReportProgress(100); return; }
            int start = previousProgress;
            EncodeWebPPass(webPQuality, 0, duration, percent => ReportProgress(start + percent * (100 - start) / 100), aborted);
        }
        else
        {
            EncodeWebPPass(webPQuality, 0, duration, ReportProgress, aborted);
        }
        ReportProgress(100);
        if (aborted()) throw new OperationCanceledException("WebP encoding aborted.");
    }

    /// <returns>False when <paramref name="sizeLimit"/> was exceeded and encoding stopped early.</returns>
    private bool EncodeWebPPass(int quality, long sizeLimit, long duration, Action<int> progress, Func<bool> aborted)
    {
        SKWebpEncoderOptions encoderOptions = SkiaImageHelpers.GetWebPEncoderOptions(quality, quality == 100, WebPLosslessEffort);
        cache.Position = 0;
        using BinaryReader reader = new(cache, System.Text.Encoding.UTF8, true);
        using SKBitmap first = new(imageInfo), second = new(imageInfo);
        SKBitmap current = first, previous = second;
        using AnimatedWebPCreator creator = new(outputPath, imageInfo.Width, imageInfo.Height);
        // Frames are independent once cropped, so encode several at once and write them in order.
        int maxInFlight = Math.Clamp(Environment.ProcessorCount - 1, 1, 8);
        Queue<WebPFrame> ready = new();
        WebPFrame? pending = null;
        long writtenMilliseconds = 0;
        bool WriteReady(int keep)
        {
            while (ready.Count > keep)
            {
                WebPFrame frame = ready.Dequeue();
                WriteWebPFrame(creator, frame.Data.GetAwaiter().GetResult(), frame.Bounds, frame.Until, ref writtenMilliseconds);
                if (sizeLimit > 0 && creator.Length > sizeLimit) return false;
            }
            return true;
        }
        for (long index = 0; index < frameCount; index++)
        {
            if (aborted()) throw new OperationCanceledException("WebP encoding aborted.");
            long timestamp = ReadFrame(reader, current);
            Rectangle bounds = index == 0 ? new(0, 0, imageInfo.Width, imageInfo.Height) : AnimatedWebPCreator.GetChangedBounds(previous, current);
            if (!bounds.IsEmpty)
            {
                if (pending != null)
                {
                    pending.Until = timestamp;
                    ready.Enqueue(pending);
                    if (!WriteReady(maxInFlight - 1)) return false;
                }
                SKBitmap crop = AnimatedWebPCreator.CopyRegion(current, bounds);
                pending = new(Task.Run(() => EncodeWebP(crop, encoderOptions)), bounds);
            }
            (current, previous) = (previous, current);
            progress((int)((index + 1) * 99 / frameCount));
        }
        if (aborted()) throw new OperationCanceledException("WebP encoding aborted.");
        if (pending != null)
        {
            pending.Until = duration;
            ready.Enqueue(pending);
        }
        return WriteReady(0);
    }

    private static byte[] EncodeWebP(SKBitmap bitmap, SKWebpEncoderOptions options)
    {
        using (bitmap)
        {
            using MemoryStream stream = new();
            SkiaImageHelpers.SaveWebP(bitmap, stream, options);
            return stream.ToArray();
        }
    }

    private static void WriteWebPFrame(AnimatedWebPCreator creator, byte[] webp, Rectangle bounds, long until, ref long writtenMilliseconds)
    {
        // Round cumulative timestamps so frame durations do not drift, preserving dropped slots.
        long end = Math.Max(writtenMilliseconds + 1, (until + TimeSpan.TicksPerMillisecond / 2) / TimeSpan.TicksPerMillisecond);
        while (writtenMilliseconds < end)
        {
            int delay = (int)Math.Min(end - writtenMilliseconds, AnimatedWebPCreator.MaxDuration);
            creator.AddFrame(webp, bounds.Location, bounds.Size, delay);
            writtenMilliseconds += delay;
        }
    }
}
