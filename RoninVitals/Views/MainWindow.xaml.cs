using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using RoninVitals.ViewModels;

namespace RoninVitals.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ShellViewModel();

        var descriptor = DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty, typeof(ContentControl));
        descriptor.AddValueChanged(ScreenHost, (_, _) => AnimateScreenTransition());
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
