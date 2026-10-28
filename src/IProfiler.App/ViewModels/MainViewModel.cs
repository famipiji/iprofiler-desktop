using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using IProfiler.Core.Domain;
using IProfiler.Core.Scanning;
using Microsoft.Extensions.Logging;

namespace IProfiler.App.ViewModels;

public sealed class MainViewModel(IScanProvider scanner, ILogger<MainViewModel> logger) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<Page> Pages { get; } = [];

    public string Status
    {
        get;
        private set
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    } = "Ready";

    /// <summary>Scans from the first available device into <see cref="Pages"/>.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Status = "Scanning...";
        Pages.Clear();

        try
        {
            var devices = await scanner.GetDevicesAsync(cancellationToken);
            if (devices.Count == 0)
            {
                Status = "No scanner found.";
                return;
            }

            var device = devices[0];
            await foreach (var page in scanner.ScanAsync(new ScanRequest(device.Id), cancellationToken))
                Pages.Add(page);

            Status = $"{Pages.Count} pages from {device.Name}";
            logger.LogInformation("Loaded {PageCount} pages from {Device}", Pages.Count, device.Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Scan failed");
            Status = "Scan failed; see the log for details.";
        }
    }
}
