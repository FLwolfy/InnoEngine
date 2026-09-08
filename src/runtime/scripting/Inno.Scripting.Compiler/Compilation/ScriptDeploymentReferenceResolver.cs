using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Inno.Scripting.Compiler;

internal sealed record ScriptDeploymentReferenceSet(
    IReadOnlyList<string> paths,
    string fingerprint);

internal static class ScriptDeploymentReferenceResolver
{
    internal static ScriptDeploymentReferenceSet Resolve(string runtimeDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);
        string normalizedDirectory = Path.GetFullPath(runtimeDirectory);
        if (!Directory.Exists(normalizedDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The target Player runtime directory '{normalizedDirectory}' does not exist.");
        }

        string[] paths = Directory
            .EnumerateFiles(normalizedDirectory, "Inno.*.dll", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
        {
            throw new InvalidDataException(
                $"The target Player runtime directory '{normalizedDirectory}' contains no Inno runtime assemblies.");
        }

        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(fingerprint, "Inno.ScriptDeploymentReferences");
        foreach (string path in paths)
        {
            AssemblyName identity;
            try
            {
                identity = AssemblyName.GetAssemblyName(path);
            }
            catch (Exception exception) when (exception is IOException or BadImageFormatException)
            {
                throw new InvalidDataException(
                    $"Target Player runtime assembly '{Path.GetFileName(path)}' is not valid managed metadata.",
                    exception);
            }

            string simpleName = identity.Name
                ?? throw new InvalidDataException(
                    $"Target Player runtime assembly '{Path.GetFileName(path)}' has no assembly identity.");
            if (!identities.Add(simpleName))
            {
                throw new InvalidDataException(
                    $"The target Player runtime contains more than one '{simpleName}' assembly identity.");
            }

            Append(fingerprint, Path.GetFileName(path));
            using FileStream stream = File.OpenRead(path);
            fingerprint.AppendData(SHA256.HashData(stream));
        }

        return new ScriptDeploymentReferenceSet(
            paths,
            Convert.ToHexString(fingerprint.GetHashAndReset()));
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }
}
