using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets.Pipeline;

namespace Inno.Audio.Assets;

/// <summary>
/// Imports standard encoded audio sources into compact runtime metadata and immutable encoded-data artifacts.
/// </summary>
[AssetImporterExtension]
public sealed class AudioClipImporter : AssetImporter<AudioClipAsset>
{
    /// <summary>
    /// Gets the stable importer identity used for standard encoded audio clips.
    /// </summary>
    public override string importerId => "inno.audio.clip";

    /// <summary>
    /// Gets the source extensions decoded by the standard audio metadata reader.
    /// </summary>
    public override IReadOnlyList<string> supportedExtensions { get; } = [".wav", ".flac", ".mp3"];

    /// <summary>
    /// Validates source metadata and emits the compact runtime and immutable encoded-data artifacts.
    /// </summary>
    /// <param name="context">
    /// Source bytes and normalized source extension for the current import.
    /// </param>
    /// <param name="output">
    /// Writer that receives the audio asset and its two runtime artifacts.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to cancel artifact writes.
    /// </param>
    /// <returns>
    /// A task that completes after both artifacts have been committed.
    /// </returns>
    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<AudioClipAsset> output,
        CancellationToken cancellationToken)
    {
        AudioClipMetadata metadata = context.extension switch
        {
            ".wav" => ReadWav(context.sourceBytes.Span),
            ".flac" => ReadFlac(context.sourceBytes.Span),
            ".mp3" => ReadMp3(context.sourceBytes.Span),
            _ => throw new InvalidDataException($"Unsupported audio extension '{context.extension}'.")
        };
        output.SetAsset(new AudioClipAsset());
        await output.WriteArtifactAsync(
            "runtime",
            AudioClipMetadataCodec.Encode(metadata),
            cancellationToken).ConfigureAwait(false);
        await output.WriteArtifactAsync("audio-data", context.sourceBytes, cancellationToken).ConfigureAwait(false);
    }

    private static AudioClipMetadata ReadWav(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes[8..12].SequenceEqual("WAVE"u8))
            throw new InvalidDataException("The WAV source has an invalid RIFF header.");

        int channels = 0;
        int sampleRate = 0;
        int blockAlignment = 0;
        long dataLength = -1;
        int offset = 12;
        while (offset <= bytes.Length - 8)
        {
            ReadOnlySpan<byte> chunkId = bytes.Slice(offset, 4);
            uint chunkLengthValue = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 4, 4));
            if (chunkLengthValue > int.MaxValue)
                throw new InvalidDataException("The WAV source contains an oversized chunk.");
            int chunkLength = (int)chunkLengthValue;
            int chunkStart = checked(offset + 8);
            int chunkEnd = checked(chunkStart + chunkLength);
            if (chunkEnd > bytes.Length)
                throw new InvalidDataException("The WAV source contains a truncated chunk.");

            if (chunkId.SequenceEqual("fmt "u8))
            {
                if (chunkLength < 16)
                    throw new InvalidDataException("The WAV format chunk is truncated.");
                ushort format = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(chunkStart, 2));
                if (format is not 1 and not 3 and not 0xfffe)
                    throw new InvalidDataException($"The WAV format code '{format}' is not supported.");
                channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(chunkStart + 2, 2));
                sampleRate = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(chunkStart + 4, 4)));
                blockAlignment = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(chunkStart + 12, 2));
            }
            else if (chunkId.SequenceEqual("data"u8))
            {
                dataLength = chunkLength;
            }

            offset = checked(chunkEnd + (chunkLength & 1));
        }

        if (channels <= 0 || sampleRate <= 0 || blockAlignment <= 0 || dataLength < 0)
            throw new InvalidDataException("The WAV source is missing valid format or data chunks.");
        return new AudioClipMetadata(
            AudioCodecId.wav,
            channels,
            sampleRate,
            dataLength / blockAlignment,
            bytes.Length);
    }

    private static AudioClipMetadata ReadFlac(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 42 || !bytes[..4].SequenceEqual("fLaC"u8))
            throw new InvalidDataException("The FLAC source has an invalid stream marker.");
        int offset = 4;
        bool foundStreamInfo = false;
        int channels = 0;
        int sampleRate = 0;
        long frameCount = 0;
        while (offset <= bytes.Length - 4)
        {
            byte header = bytes[offset];
            int blockType = header & 0x7f;
            int length = (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
            int dataStart = checked(offset + 4);
            int dataEnd = checked(dataStart + length);
            if (dataEnd > bytes.Length)
                throw new InvalidDataException("The FLAC source contains a truncated metadata block.");
            if (blockType == 0)
            {
                if (foundStreamInfo || length != 34)
                    throw new InvalidDataException("The FLAC STREAMINFO block is invalid.");
                ReadOnlySpan<byte> packed = bytes.Slice(dataStart + 10, 8);
                ulong value = BinaryPrimitives.ReadUInt64BigEndian(packed);
                sampleRate = (int)((value >> 44) & 0xfffff);
                channels = (int)((value >> 41) & 0x7) + 1;
                frameCount = (long)(value & 0xfffffffffUL);
                foundStreamInfo = true;
            }
            offset = dataEnd;
            if ((header & 0x80) != 0)
                break;
        }
        if (!foundStreamInfo || channels <= 0 || sampleRate <= 0 || frameCount <= 0)
            throw new InvalidDataException("The FLAC source is missing valid STREAMINFO metadata.");
        return new AudioClipMetadata(AudioCodecId.flac, channels, sampleRate, frameCount, bytes.Length);
    }

    private static AudioClipMetadata ReadMp3(ReadOnlySpan<byte> bytes)
    {
        int offset = SkipId3(bytes);
        int channels = 0;
        int sampleRate = 0;
        long frameCount = 0;
        int encodedFrameCount = 0;
        while (offset <= bytes.Length - 4)
        {
            uint header = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
            if (!TryReadMp3Frame(header, out int frameLength, out int frameSampleRate, out int samples, out int frameChannels))
            {
                if (encodedFrameCount == 0)
                {
                    offset++;
                    continue;
                }
                break;
            }
            if (offset + frameLength > bytes.Length)
                throw new InvalidDataException("The MP3 source contains a truncated audio frame.");
            sampleRate = frameSampleRate;
            channels = frameChannels;
            frameCount += samples;
            encodedFrameCount++;
            offset += frameLength;
        }
        if (encodedFrameCount == 0)
            throw new InvalidDataException("The MP3 source contains no valid Layer III frame.");
        return new AudioClipMetadata(AudioCodecId.mp3, channels, sampleRate, frameCount, bytes.Length);
    }

    private static int SkipId3(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 10 || !bytes[..3].SequenceEqual("ID3"u8))
            return 0;
        for (int index = 6; index < 10; index++)
        {
            if ((bytes[index] & 0x80) != 0)
                throw new InvalidDataException("The MP3 ID3 size is invalid.");
        }
        int size = (bytes[6] << 21) | (bytes[7] << 14) | (bytes[8] << 7) | bytes[9];
        int offset = checked(10 + size);
        if (offset > bytes.Length)
            throw new InvalidDataException("The MP3 ID3 tag is truncated.");
        return offset;
    }

    private static bool TryReadMp3Frame(
        uint header,
        out int frameLength,
        out int sampleRate,
        out int samples,
        out int channels)
    {
        frameLength = 0;
        sampleRate = 0;
        samples = 0;
        channels = 0;
        if ((header & 0xffe00000) != 0xffe00000)
            return false;
        int mpegBits = (int)((header >> 19) & 0x3);
        int layerBits = (int)((header >> 17) & 0x3);
        int bitrateIndex = (int)((header >> 12) & 0xf);
        int sampleRateIndex = (int)((header >> 10) & 0x3);
        if (mpegBits == 1 || layerBits != 1 || bitrateIndex is 0 or 15 || sampleRateIndex == 3)
            return false;

        int[] sampleRates = [44100, 48000, 32000];
        sampleRate = sampleRates[sampleRateIndex];
        bool mpegOne = mpegBits == 3;
        if (mpegBits == 2)
            sampleRate /= 2;
        else if (mpegBits == 0)
            sampleRate /= 4;
        int[] mpegOneBitrates = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
        int[] lowerBitrates = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
        int bitrate = (mpegOne ? mpegOneBitrates : lowerBitrates)[bitrateIndex];
        int padding = (int)((header >> 9) & 1);
        frameLength = ((mpegOne ? 144000 : 72000) * bitrate / sampleRate) + padding;
        samples = mpegOne ? 1152 : 576;
        channels = ((header >> 6) & 0x3) == 3 ? 1 : 2;
        return frameLength > 4;
    }
}
