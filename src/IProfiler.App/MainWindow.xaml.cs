using System.Windows;
using IProfiler.App.ViewModels;

namespace IProfiler.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        Loaded += OnLoaded;
    }

    // LoadAsync logs and reports its own failures, so nothing escapes this async void handler.
    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.LoadAsync();
}
