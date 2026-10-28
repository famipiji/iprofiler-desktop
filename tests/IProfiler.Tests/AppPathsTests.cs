using IProfiler.Core;

namespace IProfiler.Tests;

public class AppPathsTests
{
    [Fact]
    public void Default_uses_the_exe_folder_and_local_app_data()
    {
        var paths = AppPaths.Default();

        Assert.Equal(Path.GetFullPath(AppContext.BaseDirectory), paths.Install);
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iProfiler"),
            paths.Data);
    }

    [Fact]
    public void Working_folders_live_under_data_and_models_under_install()
    {
        var paths = new AppPaths(@"C:\Program Files\iProfiler", @"C:\Users\x\AppData\Local\iProfiler");

        Assert.Equal(@"C:\Users\x\AppData\Local\iProfiler\logs", paths.Logs);
        Assert.Equal(@"C:\Users\x\AppData\Local\iProfiler\scans", paths.Scans);
        Assert.Equal(@"C:\Users\x\AppData\Local\iProfiler\iprofiler.db", paths.Database);
        Assert.Equal(@"C:\Program Files\iProfiler\models", paths.Models);
    }
}
