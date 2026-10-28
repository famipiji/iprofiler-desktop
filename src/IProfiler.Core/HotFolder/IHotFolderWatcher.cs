namespace IProfiler.Core.HotFolder;

public interface IHotFolderWatcher
{
    /// <summary>
    /// Yields the full path of each file in <paramref name="folderPath"/> once it is completely
    /// written and ready to import. Runs until <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    IAsyncEnumerable<string> WatchAsync(string folderPath, CancellationToken cancellationToken = default);
}
