using System.Buffers.Binary;

namespace Vam.Engine.Windows.Shell;

/// <summary>Turns the bytes of an .ico file into an icon handle of one size.</summary>
static unsafe class IconFile
{
    const int HeaderBytes = 6;
    const int EntryBytes = 16;
    const uint IconVersion = 0x00030000;

    /// <summary>Loads the image closest to <paramref name="size"/>, preferring one larger than it.</summary>
    /// <param name="file">The whole .ico file.</param>
    /// <param name="size">Pixels square.</param>
    /// <returns>The handle, or zero. The caller destroys it.</returns>
    internal static nint Load(byte[] file, int size)
    {
        if (file.Length < HeaderBytes)
        {
            return 0;
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(4));
        int best = -1;
        int bestSize = 0;

        for (int index = 0; index < count && HeaderBytes + (EntryBytes * (index + 1)) <= file.Length; index++)
        {
            // Zero in the width byte means 256.
            byte stored = file[HeaderBytes + (EntryBytes * index)];
            int width = stored == 0 ? 256 : stored;

            if (best < 0 || (width >= size && (bestSize < size || width < bestSize)) || (bestSize < size && width > bestSize))
            {
                best = index;
                bestSize = width;
            }
        }

        if (best < 0)
        {
            return 0;
        }

        ReadOnlySpan<byte> entry = file.AsSpan(HeaderBytes + (EntryBytes * best), EntryBytes);
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);

        if ((ulong)offset + length > (ulong)file.Length)
        {
            return 0;
        }

        fixed (byte* bits = &file[offset])
        {
            return NativeMethods.CreateIconFromResourceEx(bits, length, true, IconVersion, size, size, 0);
        }
    }
}
