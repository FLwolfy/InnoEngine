using System;
using System.Security.Cryptography;
using System.Text;

namespace Inno.Editor.Scripting;

internal static class ScriptTypeIdentity
{
    private static readonly Guid C_SCRIPT_SOURCE_NAMESPACE =
        Guid.Parse("6ca29f90-69d4-4f27-b42b-fdad7cc10e6a");

    internal static Guid CreateCanonical(Guid sourcePersistentId)
    {
        if (sourcePersistentId == Guid.Empty)
            throw new ArgumentException("A script source identity is required.", nameof(sourcePersistentId));
        return CreateGuidV5(C_SCRIPT_SOURCE_NAMESPACE, sourcePersistentId.ToString("D"));
    }

    private static Guid CreateGuidV5(Guid namespaceId, string name)
    {
        byte[] namespaceBytes = namespaceId.ToByteArray();
        SwapGuidByteOrder(namespaceBytes);
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        byte[] data = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, data, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, data, namespaceBytes.Length, nameBytes.Length);

        byte[] hash = SHA1.HashData(data);
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        byte[] guidBytes = bytes.ToArray();
        SwapGuidByteOrder(guidBytes);
        return new Guid(guidBytes);
    }

    private static void SwapGuidByteOrder(byte[] bytes)
    {
        (bytes[0], bytes[3]) = (bytes[3], bytes[0]);
        (bytes[1], bytes[2]) = (bytes[2], bytes[1]);
        (bytes[4], bytes[5]) = (bytes[5], bytes[4]);
        (bytes[6], bytes[7]) = (bytes[7], bytes[6]);
    }
}
