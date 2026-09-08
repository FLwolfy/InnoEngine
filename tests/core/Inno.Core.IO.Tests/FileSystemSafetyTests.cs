using System;
using System.IO;
using System.Linq;
using System.Text;

using Xunit;

namespace Inno.Core.IO.Tests;

public sealed class FileSystemSafetyTests : IDisposable
{
    private readonly string m_root = Path.Combine(
        Path.GetTempPath(),
        "InnoCoreIoTests",
        Guid.NewGuid().ToString("N"));

    public FileSystemSafetyTests()
        => Directory.CreateDirectory(m_root);

    [Fact]
    public void WriteAllBytesReplacesTheCompletePayloadWithoutLeavingStagingFiles()
    {
        string destination = Path.Combine(m_root, "Settings.Project.inno");
        File.WriteAllText(destination, "previous");

        AtomicFile.WriteAllBytes(destination, "current"u8);

        Assert.Equal("current", File.ReadAllText(destination));
        Assert.Empty(Directory.EnumerateFiles(m_root).Where(path => path.Contains(".staging-", StringComparison.Ordinal)));
    }

    [Fact]
    public void InstallRequiresASameDirectoryCandidateAndPreservesTheDestinationOnFailure()
    {
        string candidateDirectory = Path.Combine(m_root, "candidate");
        Directory.CreateDirectory(candidateDirectory);
        string candidate = Path.Combine(candidateDirectory, "document.inno");
        string destination = Path.Combine(m_root, "document.inno");
        File.WriteAllText(candidate, "candidate");
        File.WriteAllText(destination, "current");

        Assert.Throws<ArgumentException>(() => AtomicFile.Install(candidate, destination));

        Assert.Equal("current", File.ReadAllText(destination));
        Assert.Equal("candidate", File.ReadAllText(candidate));
    }

    [Fact]
    public void InstallReplacesAnExistingFileWithTheCompleteCandidate()
    {
        string candidate = Path.Combine(m_root, "document.staging");
        string destination = Path.Combine(m_root, "document.inno");
        File.WriteAllText(candidate, "candidate");
        File.WriteAllText(destination, "current");

        AtomicFile.Install(candidate, destination);

        Assert.False(File.Exists(candidate));
        Assert.Equal("candidate", File.ReadAllText(destination));
    }

    [Fact]
    public void PathBoundaryRejectsAbsoluteAndParentTraversalPaths()
    {
        Assert.Throws<ArgumentException>(() => PathBoundary.Resolve(m_root, Path.Combine(m_root, "absolute")));
        Assert.Throws<IOException>(() => PathBoundary.Resolve(m_root, "../escape"));
        Assert.Equal(
            Path.Combine(m_root, "Assets", "item.inno"),
            PathBoundary.Resolve(m_root, "Assets/item.inno"));
    }

    [Fact]
    public void DirectoryInstallReplacesACompleteTreeWithoutLeavingBackupDirectories()
    {
        string candidate = Path.Combine(m_root, "candidate");
        string destination = Path.Combine(m_root, "destination");
        Directory.CreateDirectory(candidate);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(candidate, "current.txt"), "current", Encoding.UTF8);
        File.WriteAllText(Path.Combine(destination, "previous.txt"), "previous", Encoding.UTF8);

        AtomicDirectory.Install(candidate, destination);

        Assert.False(Directory.Exists(candidate));
        Assert.False(File.Exists(Path.Combine(destination, "previous.txt")));
        Assert.Equal("current", File.ReadAllText(Path.Combine(destination, "current.txt"), Encoding.UTF8));
        Assert.Empty(Directory.EnumerateDirectories(m_root).Where(path => path.Contains(".backup-", StringComparison.Ordinal)));
    }

    public void Dispose()
    {
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }
}
