using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using RoninVitals.ViewModels;

namespace RoninVitals.Views;

public partial class MainWindow : Window
{
    private bool _closeApproved;
    private bool _closeQuestionOpen;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ShellViewModel();

        var descriptor = DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty, typeof(ContentControl));
        descriptor.AddValueChanged(ScreenHost, (_, _) => AnimateScreenTransition());
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closeApproved || e.Cancel) return;

        // Hold the close while the current screen is asked. A second request made while the question is
        // open is ignored, so the screen is never asked twice at once.
        e.Cancel = true;
        if (_closeQuestionOpen) return;

        _closeQuestionOpen = true;
        try
        {
            if (DataContext is not ShellViewModel shell || await shell.CanCloseAsync())
            {
                _closeApproved = true;
                Close();
            }
        }
        finally
        {
            _closeQuestionOpen = false;
        }
    }

    private void AnimateScreenTransition()
    {
        ScreenHost.Opacity = 0;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ScreenHost.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }
}
