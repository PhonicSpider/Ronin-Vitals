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

        if (_closeQuestionOpen)
        {
            // The user asked to close again while the first question is still open. Let this request
            // through rather than ignoring it, so a screen that never answers cannot make the window
            // impossible to close. The first question's answer is dropped when it arrives (see below).
            _closeApproved = true;
            return;
        }

        e.Cancel = true;
        _closeQuestionOpen = true;
        try
        {
            if (DataContext is not ShellViewModel shell || await shell.CanCloseAsync())
            {
                // A second request may already have closed the window while this answer was pending.
                if (_closeApproved) return;
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
