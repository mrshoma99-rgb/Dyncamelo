using System;
using System.IO;
using System.IO.Compression;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes.Imaging;

/// <summary>
/// A tiny, dependency-free PNG encoder for 8-bit RGBA rasters. Kept in the
/// portable node library (netstandard2.0) so the whole imaging path — including
/// the heat-map render — is unit-testable off Windows, without System.Drawing.
/// Writes a single-IDAT, non-interlaced, truecolour-with-alpha image.
/// Infrastructure helper, not a node.
/// </summary>
[IsVisibleInLibrary(false)]
public static class PngWriter
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    /// <summary>Encodes a width×height RGBA buffer (row-major, 4 bytes/pixel) as PNG bytes.</summary>
    /// <param name="width">Image width in pixels (must be positive).</param>
    /// <param name="height">Image height in pixels (must be positive).</param>
    /// <param name="rgba">The pixel buffer: width*height*4 bytes, R,G,B,A per pixel, top row first.</param>
    /// <returns>The complete PNG file as a byte array.</returns>
    public static byte[] Encode(int width, int height, byte[] rgba)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Image width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Image height must be positive.");
        }

        if (rgba == null)
        {
            throw new ArgumentNullException(nameof(rgba));
        }

        var expected = checked(width * height * 4);
        if (rgba.Length != expected)
        {
            throw new ArgumentException(
                "The pixel buffer is " + rgba.Length + " bytes but " + expected +
                " were expected for a " + width + "×" + height + " RGBA image.", nameof(rgba));
        }

        using var output = new MemoryStream();
        output.Write(Signature, 0, Signature.Length);

        // IHDR: 8-bit depth, colour type 6 (truecolour + alpha), no interlace.
        var ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, (uint)width);
        WriteBigEndian(ihdr, 4, (uint)height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 6;   // colour type RGBA
        ihdr[10] = 0;  // compression
        ihdr[11] = 0;  // filter
        ihdr[12] = 0;  // interlace
        WriteChunk(output, "IHDR", ihdr);

        WriteChunk(output, "IDAT", CompressScanlines(width, height, rgba));
        WriteChunk(output, "IEND", Array.Empty<byte>());

        return output.ToArray();
    }

    /// <summary>Filters each scanline (filter type 0 = None) and wraps DEFLATE in a zlib stream.</summary>
    private static byte[] CompressScanlines(int width, int height, byte[] rgba)
    {
        var stride = width * 4;
        var raw = new byte[height * (stride + 1)];
        for (int y = 0; y < height; y++)
        {
            var dst = y * (stride + 1);
            raw[dst] = 0; // filter: None
            Array.Copy(rgba, y * stride, raw, dst + 1, stride);
        }

        using var compressed = new MemoryStream();
        // zlib header: 0x78 0x01 (deflate, 32K window, no dict, fastest).
        compressed.WriteByte(0x78);
        compressed.WriteByte(0x01);
        using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw, 0, raw.Length);
        }

        var adler = Adler32(raw);
        compressed.WriteByte((byte)(adler >> 24));
        compressed.WriteByte((byte)(adler >> 16));
        compressed.WriteByte((byte)(adler >> 8));
        compressed.WriteByte((byte)adler);
        return compressed.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        stream.Write(length, 0, 4);

        var typeBytes = new byte[4];
        for (int i = 0; i < 4; i++)
        {
            typeBytes[i] = (byte)type[i];
        }

        stream.Write(typeBytes, 0, 4);
        stream.Write(data, 0, data.Length);

        var crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        WriteBigEndian(crcBytes, 0, crc);
        stream.Write(crcBytes, 0, 4);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Adler32(byte[] data)
    {
        const uint mod = 65521;
        uint a = 1, b = 0;
        foreach (var value in data)
        {
            a = (a + value) % mod;
            b = (b + a) % mod;
        }

        return (b << 16) | a;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in type)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        foreach (var value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
