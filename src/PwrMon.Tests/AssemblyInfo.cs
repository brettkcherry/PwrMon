using System.Runtime.CompilerServices;
using PwrMon.Services;
using Xunit;

// AppSettings.Current is a process-wide static shared by UnitFormatter tests; disabling
// parallelization keeps those tests from racing each other (or anything else) over it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PwrMon.Tests;

/// <summary>
/// Extends TESTING.md's "never touch the user's real settings/history" rule to cover the log.
///
/// No test logs on purpose, so the rule as written didn't catch this: the services under test
/// call <see cref="Log"/> themselves, which resolves to the user's real
/// <c>%LocalAppData%\PwrMon\logs\</c>. A run appended synthetic "drain-on-AC alert" lines to the
/// live diagnostic log, and on 2026-08-19 those were briefly read as the real incident during
/// an investigation into it.
///
/// A module initializer rather than a fixture: it has to run before the first test touches
/// anything, and xUnit fixtures don't guarantee that ordering across every collection.
/// </summary>
internal static class TestLogRedirect
{
    [ModuleInitializer]
    internal static void RedirectLogAwayFromRealUserData()
    {
        Log.DirectoryOverride = Path.Combine(
            Path.GetTempPath(), "PwrMon-tests", $"run-{Guid.NewGuid():N}", "logs");
    }
}
