using System.Runtime.CompilerServices;
using BoostOS.Core;

namespace BoostOS.Tests;

internal static class TestSetup
{
    // Tests exercise failure paths on purpose; their log lines must not land in the real %LOCALAPPDATA%\BoostOS\log.txt.
    [ModuleInitializer]
    internal static void KeepTheRealLogClean() => Log.Folder = Path.Combine(Path.GetTempPath(), "boostos-tests");
}
