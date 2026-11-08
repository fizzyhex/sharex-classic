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

using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace ShareX.HelpersLib;

/// <summary>Decodes animation frames together with the prior frames required by their disposal rules.</summary>
public sealed class SkiaAnimatedImage : IDisposable
{
    private readonly SKData data;
    private readonly SKCodec codec;
    public int FrameCount => Math.Max(1, codec.FrameCount);
    public int Width => codec.Info.Width;
    public int Height => codec.Info.Height;
    public SKEncodedImageFormat Format => codec.EncodedFormat;
    /// <summary>Extra plays after the first; -1 repeats forever.</summary>
    public int RepetitionCount => codec.RepetitionCount;

    public int GetFrameDuration(int index) => FrameCount > 1 ? codec.FrameInfo[index].Duration : 0;

    public SkiaAnimatedImage(string filePath)
    {
        data = SKData.CreateCopy(File.ReadAllBytes(filePath));
        codec = SKCodec.Create(data);
        if (codec == null)
        {
            data.Dispose();
            throw new InvalidDataException("The image format is not supported.");
        }
    }

    public SKBitmap GetFrame(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, FrameCount);
        SKBitmap bitmap = SkiaImageHelpers.CreateBitmap(Width, Height);
        try
        {
            SKCodecFrameInfo[] info = codec.FrameInfo;
            Stack<int> frames = new();
            int required = index;
            while (required >= 0)
            {
                frames.Push(required);
                required = info.Length > required ? info[required].RequiredFrame : -1;
            }
            int previous = -1;
            while (frames.Count > 0)
            {
                int frame = frames.Pop();
                SKCodecResult result = codec.GetPixels(bitmap.Info, bitmap.GetPixels(), new SKCodecOptions(frame, previous));
                if (result is not SKCodecResult.Success and not SKCodecResult.IncompleteInput)
                    throw new InvalidDataException($"Frame {frame} decoding failed: {result}.");
                previous = frame;
            }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>Decode a run of frames in order, reusing each frame to build the next.</summary>
    /// <remarks>The same bitmap is yielded every time; it is only valid until the next frame is requested.</remarks>
    public IEnumerable<SKBitmap> GetFrames(int first, int endExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(endExclusive, FrameCount);
        if (first >= endExclusive) yield break;
        using SKBitmap bitmap = GetFrame(first);
        yield return bitmap;
        SKCodecFrameInfo[] info = codec.FrameInfo;
        for (int frame = first + 1; frame < endExclusive; frame++)
        {
            // The bitmap holds the previous frame, which satisfies any frame that depends on it or earlier.
            int prior = info.Length > frame && info[frame].RequiredFrame < 0 ? -1 : frame - 1;
            SKCodecResult result = codec.GetPixels(bitmap.Info, bitmap.GetPixels(), new SKCodecOptions(frame, prior));
            if (result is not SKCodecResult.Success and not SKCodecResult.IncompleteInput)
                throw new InvalidDataException($"Frame {frame} decoding failed: {result}.");
            bitmap.NotifyPixelsChanged();
            yield return bitmap;
        }
    }

    public void Dispose()
    {
        codec.Dispose();
        data.Dispose();
    }
}
