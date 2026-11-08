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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace ShareX.HelpersLib;

/// <summary>Writes an animated WebP container (VP8X, ANIM, ANMF) from single-frame WebP images.</summary>
public sealed class AnimatedWebPCreator : IDisposable
{
    public const int MaxDimension = 16384;
    public const int MaxDuration = 0xFFFFFF;
    private const int HeaderSize = 12;
    private const int VP8XFlagsOffset = HeaderSize + 8;
    private const byte AlphaFlag = 0x10, AnimationFlag = 0x02;
    private const byte DoNotBlend = 0x02;
    private readonly FileStream stream;
    private readonly int width, height;
    private bool hasAlpha, disposed;

    /// <summary>Bytes written so far.</summary>
    public long Length => stream.Length;

    /// <param name="loopCount">Number of times to play the animation; 0 repeats forever.</param>
    public AnimatedWebPCreator(string path, int width, int height, int loopCount = 0)
    {
        if (width is < 1 or > MaxDimension) throw new ArgumentOutOfRangeException(nameof(width));
        if (height is < 1 or > MaxDimension) throw new ArgumentOutOfRangeException(nameof(height));
        if (loopCount is < 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(loopCount));
        this.width = width;
        this.height = height;
        stream = new(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1024 * 1024);
        Span<byte> header = stackalloc byte[HeaderSize + 18 + 14];
        "RIFF"u8.CopyTo(header);
        "WEBP"u8.CopyTo(header[8..]);
        Span<byte> vp8x = header[HeaderSize..];
        "VP8X"u8.CopyTo(vp8x);
        BinaryPrimitives.WriteUInt32LittleEndian(vp8x[4..], 10);
        vp8x[8] = AnimationFlag;
        WriteUInt24(vp8x[12..], width - 1);
        WriteUInt24(vp8x[15..], height - 1);
        Span<byte> anim = vp8x[18..];
        "ANIM"u8.CopyTo(anim);
        BinaryPrimitives.WriteUInt32LittleEndian(anim[4..], 6);
        // Background color is only a hint to viewers; frames always cover what they change.
        BinaryPrimitives.WriteUInt32LittleEndian(anim[8..], 0xFF000000);
        BinaryPrimitives.WriteUInt16LittleEndian(anim[12..], (ushort)loopCount);
        stream.Write(header);
    }

    /// <summary>Encode a full canvas frame, storing only the area that changed since <paramref name="previous"/>.</summary>
    public void AddFrame(SKBitmap frame, SKBitmap previous, int durationMilliseconds, SKWebpEncoderOptions options)
    {
        if (frame.Width != width || frame.Height != height) throw new ArgumentException("Frames must match the canvas size.", nameof(frame));
        Rectangle bounds = previous == null ? new(0, 0, width, height) : GetChangedBounds(previous, frame);
        // Keep the frame's timing even when nothing changed.
        if (bounds.IsEmpty) bounds = new(0, 0, 1, 1);
        using SKBitmap crop = CopyRegion(frame, bounds);
        using MemoryStream encoded = new();
        SkiaImageHelpers.SaveWebP(crop, encoded, options);
        AddFrame(encoded.GetBuffer().AsSpan(0, (int)encoded.Length), bounds.Location, bounds.Size, durationMilliseconds);
    }

    /// <summary>Add a frame that replaces the canvas within its rectangle. The offset must be even.</summary>
    public void AddFrame(ReadOnlySpan<byte> webp, Point offset, Size size, int durationMilliseconds)
    {
        if ((offset.X | offset.Y) % 2 != 0) throw new ArgumentException("WebP frame offsets must be even.", nameof(offset));
        if (durationMilliseconds is < 0 or > MaxDuration) throw new ArgumentOutOfRangeException(nameof(durationMilliseconds));
        // A still WebP is RIFF/WEBP followed by chunks. Keep only the image data chunks;
        // VP8X, ICCP and metadata describe the file rather than the frame.
        List<Range> chunks = [];
        long frameDataSize = 0;
        for (int position = HeaderSize; position + 8 <= webp.Length;)
        {
            ReadOnlySpan<byte> fourCC = webp.Slice(position, 4);
            int chunkSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(webp[(position + 4)..]));
            int end = checked(position + 8 + chunkSize + (chunkSize & 1));
            if (end > webp.Length) throw new InvalidDataException("Truncated WebP frame.");
            bool lossless = fourCC.SequenceEqual("VP8L"u8);
            if (fourCC.SequenceEqual("ALPH"u8)) hasAlpha = true;
            // VP8L stores alpha inline: signature byte, then 14-bit width, 14-bit height and an alpha bit.
            else if (lossless && chunkSize >= 5 && ((BinaryPrimitives.ReadUInt32LittleEndian(webp[(position + 9)..]) >> 28) & 1) != 0) hasAlpha = true;
            if (lossless || fourCC.SequenceEqual("ALPH"u8) || fourCC.SequenceEqual("VP8 "u8))
            {
                chunks.Add(position..end);
                frameDataSize += end - position;
            }
            position = end;
        }
        if (chunks.Count == 0) throw new InvalidDataException("WebP frame contains no image data.");

        Span<byte> anmf = stackalloc byte[24];
        "ANMF"u8.CopyTo(anmf);
        BinaryPrimitives.WriteUInt32LittleEndian(anmf[4..], checked((uint)(16 + frameDataSize)));
        WriteUInt24(anmf[8..], offset.X / 2);
        WriteUInt24(anmf[11..], offset.Y / 2);
        WriteUInt24(anmf[14..], size.Width - 1);
        WriteUInt24(anmf[17..], size.Height - 1);
        WriteUInt24(anmf[20..], durationMilliseconds);
        anmf[23] = DoNotBlend;
        stream.Write(anmf);
        foreach (Range chunk in chunks) stream.Write(webp[chunk]);
    }

    /// <summary>Pixels that differ between two same-sized 32-bit frames, widened to the even offsets WebP requires.</summary>
    public static unsafe Rectangle GetChangedBounds(SKBitmap previous, SKBitmap current)
    {
        if (previous.Width != current.Width || previous.Height != current.Height || previous.BytesPerPixel != 4 || current.BytesPerPixel != 4)
            throw new ArgumentException("Frames must be the same size with 32-bit pixels.");
        int frameWidth = current.Width, frameHeight = current.Height;
        int left = frameWidth, top = frameHeight, right = -1, bottom = -1;
        byte* currentPixels = (byte*)current.GetPixels(), previousPixels = (byte*)previous.GetPixels();
        for (int y = 0; y < frameHeight; y++)
        {
            ReadOnlySpan<uint> row = new(currentPixels + (long)y * current.RowBytes, frameWidth);
            ReadOnlySpan<uint> before = new(previousPixels + (long)y * previous.RowBytes, frameWidth);
            int first = row.CommonPrefixLength(before);
            if (first == frameWidth) continue;
            // Only search for a new right edge when pixels beyond the known edge differ.
            if (!row[(right + 1)..].SequenceEqual(before[(right + 1)..]))
            {
                int last = frameWidth - 1;
                while (last > first && row[last] == before[last]) last--;
                right = last;
            }
            left = Math.Min(left, first);
            top = Math.Min(top, y);
            bottom = y;
        }
        return bottom < 0 ? Rectangle.Empty : Rectangle.FromLTRB(left & ~1, top & ~1, right + 1, bottom + 1);
    }

    public static SKBitmap CopyRegion(SKBitmap source, Rectangle bounds)
    {
        using SKBitmap subset = new();
        if (!source.ExtractSubset(subset, new SKRectI(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom)))
            throw new ArgumentOutOfRangeException(nameof(bounds));
        return subset.Copy() ?? throw new InvalidOperationException("Could not copy the frame region.");
    }

    private static void WriteUInt24(Span<byte> destination, int value)
    {
        destination[0] = (byte)value;
        destination[1] = (byte)(value >> 8);
        destination[2] = (byte)(value >> 16);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            long length = stream.Length;
            if (length - 8 > uint.MaxValue) throw new IOException("Animated WebP files cannot exceed 4 GB.");
            Span<byte> riffSize = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(riffSize, (uint)(length - 8));
            stream.Position = 4;
            stream.Write(riffSize);
            if (hasAlpha)
            {
                stream.Position = VP8XFlagsOffset;
                stream.WriteByte(AlphaFlag | AnimationFlag);
            }
            stream.Flush();
        }
        finally { stream.Dispose(); }
    }

    /// <summary>True for a WebP file whose header marks it as animated; reads only the first chunk header.</summary>
    public static bool IsAnimatedWebPFile(string filePath)
    {
        try
        {
            Span<byte> header = stackalloc byte[21];
            using FileStream file = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);
            return file.ReadAtLeast(header, header.Length, false) == header.Length &&
                header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8) &&
                header[12..16].SequenceEqual("VP8X"u8) && (header[20] & AnimationFlag) != 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
