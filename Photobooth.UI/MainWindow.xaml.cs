using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Photobooth.Core;

namespace Photobooth.UI;

public partial class MainWindow : Window
{
    private readonly BoothStateMachine _stateMachine;
    private bool _sessionRunning;

    public MainWindow()
    {
        InitializeComponent();

        _stateMachine = new BoothStateMachine(new MockCameraService(), new MockPrinterService());
        _stateMachine.StateChanged += state => Dispatcher.Invoke(() => ShowState(state));
        _stateMachine.CountdownTick += number => Dispatcher.Invoke(() => CountdownNumber.Text = number.ToString());
        _stateMachine.ErrorOccurred += message => Dispatcher.Invoke(() => ErrorMessage.Text = message);

        ShowState(_stateMachine.CurrentState);
    }

    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_stateMachine.CurrentState == BoothState.Idle && !_sessionRunning)
        {
            _sessionRunning = true;
            _ = RunSessionAsync();
        }
    }

    private async Task RunSessionAsync()
    {
        try
        {
            await _stateMachine.RunSessionAsync();
        }
        finally
        {
            _sessionRunning = false;
        }
    }

    private void ShowState(BoothState state)
    {
        IdleView.Visibility = state == BoothState.Idle ? Visibility.Visible : Visibility.Collapsed;
        CountdownView.Visibility = state == BoothState.Countdown ? Visibility.Visible : Visibility.Collapsed;
        CapturingView.Visibility = state == BoothState.Capturing ? Visibility.Visible : Visibility.Collapsed;
        ReviewingView.Visibility = state == BoothState.Reviewing ? Visibility.Visible : Visibility.Collapsed;
        PrintingView.Visibility = state == BoothState.Printing ? Visibility.Visible : Visibility.Collapsed;
        CompleteView.Visibility = state == BoothState.Complete ? Visibility.Visible : Visibility.Collapsed;
        ErrorView.Visibility = state == BoothState.Error ? Visibility.Visible : Visibility.Collapsed;

        if (state == BoothState.Reviewing)
        {
            LoadCapturedImage(_stateMachine.LastCapturedImagePath);
        }
    }

    private void LoadCapturedImage(string? imagePath)
    {
        CapturedImage.Source = null;
        PhotoFallback.Visibility = Visibility.Visible;

        if (string.IsNullOrWhiteSpace(imagePath) || !System.IO.File.Exists(imagePath))
        {
            return;
        }

        CapturedImage.Source = new BitmapImage(new Uri(System.IO.Path.GetFullPath(imagePath)));
        PhotoFallback.Visibility = Visibility.Collapsed;
    }
}
