using PwrMon.Services;

namespace PwrMon.Tests;

/// <summary>
/// Covers the ACL predicate that decides whether elevated autostart is safe to offer.
/// If this check is wrong in the permissive direction, PwrMon would hand a logon-time
/// admin launch to a binary any user-level process can replace — so it's worth pinning
/// against real locations rather than trusting the rights math by inspection.
/// </summary>
public class StartupHelperTests
{
    [Fact]
    public void ProgramFiles_IsNotReplaceableByNonAdmins()
    {
        // The location an installed build lands in — the whole reason elevated autostart
        // is offered at all. If this ever returns true the feature silently disappears.
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Assert.False(StartupHelper.IsReplaceableByNonAdmins(programFiles));
    }

    [Fact]
    public void LocalAppData_IsReplaceableByNonAdmins()
    {
        // Where a portable exe typically ends up, and writable by the user by definition.
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.True(StartupHelper.IsReplaceableByNonAdmins(localAppData));
    }

    [Fact]
    public void TempDirectory_IsReplaceableByNonAdmins()
    {
        Assert.True(StartupHelper.IsReplaceableByNonAdmins(Path.GetTempPath()));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void MissingPath_FailsClosed(string? path)
    {
        // No path means no proof of protection; the safe answer is "assume writable".
        Assert.True(StartupHelper.IsReplaceableByNonAdmins(path!));
    }

    [Fact]
    public void UnreadablePath_FailsClosed()
    {
        Assert.True(StartupHelper.IsReplaceableByNonAdmins(@"Z:\no\such\volume\PwrMon.exe"));
    }
}
