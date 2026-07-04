using System.Buffers.Binary;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TouchChanX.UWP;

public static class GameIconExtractor
{
    private const int PreferredIconSize = 256;

    public static byte[]? TryExtractBestPng(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            return TryExtractRawPng(path) ?? TryExtractIconPng(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or ExternalException)
        {
            Debug.WriteLine($"Failed to extract icon from {path}: {ex}");
            return null;
        }
    }

    private static byte[]? TryExtractRawPng(string path)
    {
        try
        {
            var extractor = new PeIconPngExtractor(path);
            return extractor.ExtractPngIcons()
                .OrderByDescending(static icon => icon.Width * icon.Height)
                .ThenByDescending(static icon => icon.BitCount)
                .ThenByDescending(static icon => icon.Bytes.Length)
                .FirstOrDefault()
                ?.Bytes;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            Debug.WriteLine($"Failed to extract raw PNG icon from {path}: {ex}");
            return null;
        }
    }

    private static byte[]? TryExtractIconPng(string path)
    {
        using var icon = Icon.ExtractIcon(path, 0, PreferredIconSize);
        if (icon is null)
            return null;

        using var bitmap = icon.ToBitmap();
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private sealed class PeIconPngExtractor
    {
        private const int RtIcon = 3;
        private const int RtGroupIcon = 14;

        private readonly byte[] fileBytes;
        private readonly Section[] sections;
        private readonly int resourceDirectoryOffset;

        public PeIconPngExtractor(string path)
        {
            fileBytes = File.ReadAllBytes(path);
            var headers = ReadHeaders(fileBytes);
            sections = headers.Sections;
            resourceDirectoryOffset = RvaToFileOffset(headers.ResourceDirectoryRva);
        }

        public IEnumerable<PngIcon> ExtractPngIcons()
        {
            var iconResources = EnumerateResourceData(RtIcon)
                .Where(static resource => resource.NameId.HasValue)
                .GroupBy(static resource => resource.NameId!.Value)
                .ToDictionary(static group => group.Key, static group => group.First().Data);

            foreach (var groupResource in EnumerateResourceData(RtGroupIcon))
            {
                foreach (var icon in ParseIconGroup(groupResource.Data, iconResources))
                {
                    yield return icon;
                }
            }
        }

        private static IEnumerable<PngIcon> ParseIconGroup(
            byte[] groupData,
            IReadOnlyDictionary<int, byte[]> iconResources)
        {
            if (groupData.Length < 6)
                yield break;

            var reserved = ReadUInt16(groupData, 0);
            var type = ReadUInt16(groupData, 2);
            var count = ReadUInt16(groupData, 4);
            if (reserved != 0 || type != 1 || groupData.Length < 6 + count * 14)
                yield break;

            for (var i = 0; i < count; i++)
            {
                var offset = 6 + i * 14;
                var iconId = ReadUInt16(groupData, offset + 12);

                if (!iconResources.TryGetValue(iconId, out var bytes) ||
                    !PngInfo.TryRead(bytes, out var pngInfo))
                {
                    continue;
                }

                yield return new PngIcon(pngInfo.Width, pngInfo.Height, pngInfo.BitCount, bytes);
            }
        }

        private IEnumerable<ResourceData> EnumerateResourceData(int resourceType)
        {
            var typeEntry = ReadDirectoryEntries(resourceDirectoryOffset)
                .FirstOrDefault(entry => !entry.IsNamed && entry.Id == resourceType);

            if (typeEntry is null || !typeEntry.IsDirectory)
                yield break;

            var typeDirectoryOffset = resourceDirectoryOffset + typeEntry.OffsetToDirectoryOrData;
            foreach (var resource in WalkResourceDirectory(typeDirectoryOffset, null, 0))
            {
                yield return resource;
            }
        }

        private IEnumerable<ResourceData> WalkResourceDirectory(int directoryOffset, int? nameId, int depth)
        {
            foreach (var entry in ReadDirectoryEntries(directoryOffset))
            {
                var nextNameId = depth == 0 && !entry.IsNamed ? entry.Id : nameId;
                var entryOffset = resourceDirectoryOffset + entry.OffsetToDirectoryOrData;

                if (entry.IsDirectory)
                {
                    foreach (var child in WalkResourceDirectory(entryOffset, nextNameId, depth + 1))
                    {
                        yield return child;
                    }
                }
                else
                {
                    var dataRva = ReadUInt32(fileBytes, entryOffset);
                    var size = ReadUInt32(fileBytes, entryOffset + 4);
                    var dataOffset = RvaToFileOffset(dataRva);
                    yield return new ResourceData(nextNameId, fileBytes.AsSpan(dataOffset, checked((int)size)).ToArray());
                }
            }
        }

        private IEnumerable<ResourceDirectoryEntry> ReadDirectoryEntries(int directoryOffset)
        {
            if (directoryOffset < 0 || directoryOffset + 16 > fileBytes.Length)
                yield break;

            var namedCount = ReadUInt16(fileBytes, directoryOffset + 12);
            var idCount = ReadUInt16(fileBytes, directoryOffset + 14);
            var totalCount = namedCount + idCount;

            for (var i = 0; i < totalCount; i++)
            {
                var entryOffset = directoryOffset + 16 + i * 8;
                if (entryOffset + 8 > fileBytes.Length)
                    yield break;

                var nameOrId = ReadUInt32(fileBytes, entryOffset);
                var dataOrDirectory = ReadUInt32(fileBytes, entryOffset + 4);

                yield return new ResourceDirectoryEntry(
                    IsNamed: (nameOrId & 0x8000_0000) != 0,
                    Id: unchecked((int)(nameOrId & 0xFFFF)),
                    IsDirectory: (dataOrDirectory & 0x8000_0000) != 0,
                    OffsetToDirectoryOrData: unchecked((int)(dataOrDirectory & 0x7FFF_FFFF)));
            }
        }

        private static PeHeaders ReadHeaders(byte[] bytes)
        {
            if (bytes.Length < 0x40 || ReadUInt16(bytes, 0) != 0x5A4D)
                throw new InvalidDataException("The file is not a valid MZ executable.");

            var peOffset = ReadInt32(bytes, 0x3C);
            if (peOffset <= 0 || peOffset + 0x18 >= bytes.Length || ReadUInt32(bytes, peOffset) != 0x0000_4550)
                throw new InvalidDataException("The file is not a valid PE executable.");

            var coffOffset = peOffset + 4;
            var sectionCount = ReadUInt16(bytes, coffOffset + 2);
            var optionalHeaderSize = ReadUInt16(bytes, coffOffset + 16);
            var optionalHeaderOffset = coffOffset + 20;
            var magic = ReadUInt16(bytes, optionalHeaderOffset);
            var dataDirectoryOffset = magic switch
            {
                0x10B => optionalHeaderOffset + 96,
                0x20B => optionalHeaderOffset + 112,
                _ => throw new InvalidDataException($"Unsupported PE optional header magic: 0x{magic:X}.")
            };

            var resourceDirectoryRva = ReadUInt32(bytes, dataDirectoryOffset + 8 * 2);
            if (resourceDirectoryRva == 0)
                throw new InvalidDataException("The executable does not contain a resource directory.");

            var sectionTableOffset = optionalHeaderOffset + optionalHeaderSize;
            var sections = new Section[sectionCount];
            for (var i = 0; i < sectionCount; i++)
            {
                var sectionOffset = sectionTableOffset + i * 40;
                sections[i] = new Section(
                    VirtualSize: ReadUInt32(bytes, sectionOffset + 8),
                    VirtualAddress: ReadUInt32(bytes, sectionOffset + 12),
                    RawDataSize: ReadUInt32(bytes, sectionOffset + 16),
                    RawDataPointer: ReadUInt32(bytes, sectionOffset + 20));
            }

            return new PeHeaders(resourceDirectoryRva, sections);
        }

        private int RvaToFileOffset(uint rva)
        {
            foreach (var section in sections)
            {
                var sectionSize = Math.Max(section.VirtualSize, section.RawDataSize);
                if (rva >= section.VirtualAddress && rva < section.VirtualAddress + sectionSize)
                    return checked((int)(section.RawDataPointer + rva - section.VirtualAddress));
            }

            throw new InvalidDataException($"Could not map RVA 0x{rva:X8} to a file offset.");
        }
    }

    private readonly record struct PngInfo(int Width, int Height, int BitCount)
    {
        private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        public static bool TryRead(ReadOnlySpan<byte> bytes, out PngInfo info)
        {
            info = default;
            if (!bytes.StartsWith(Signature) || bytes.Length < 33)
                return false;

            var width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4)));
            var height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4)));
            var bitCount = bytes[24] * GetChannelCount(bytes[25]);

            if (width <= 0 || height <= 0)
                return false;

            info = new PngInfo(width, height, bitCount);
            return true;
        }

        private static int GetChannelCount(byte colorType) =>
            colorType switch
            {
                0 => 1,
                2 => 3,
                3 => 1,
                4 => 2,
                6 => 4,
                _ => 4
            };
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));

    private static int ReadInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4));

    private sealed record PeHeaders(uint ResourceDirectoryRva, Section[] Sections);

    private sealed record Section(uint VirtualSize, uint VirtualAddress, uint RawDataSize, uint RawDataPointer);

    private sealed record ResourceData(int? NameId, byte[] Data);

    private sealed record ResourceDirectoryEntry(bool IsNamed, int Id, bool IsDirectory, int OffsetToDirectoryOrData);

    private sealed record PngIcon(int Width, int Height, int BitCount, byte[] Bytes);
}
