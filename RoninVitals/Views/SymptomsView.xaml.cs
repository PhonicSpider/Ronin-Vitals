using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using RoninVitals.ViewModels;

namespace RoninVitals.Views;

public partial class SymptomsView : UserControl
{
    private const int ExpandMs = 320;
    private const int CollapseMs = 200;
    private const int ConsoleShowMs = 400;
    private const int ConsoleHideMs = 250;

    private SymptomCardViewModel? _consoleTrackedCard;
    private PropertyChangedEventHandler? _consoleCardHandler;

    public SymptomsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Fires once per generated card's expand-content panel. Syncs its initial state to the
    /// card's IsExpanded (normally false - cards don't start pre-expanded) and subscribes to
    /// future changes so the panel animates open/closed instead of snapping, per each
    /// DataTemplate instance from the ItemsControl.
    /// </summary>
    private void ExpandPanel_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement panel || panel.DataContext is not SymptomCardViewModel vm) return;

        if (vm.IsExpanded)
        {
            panel.Visibility = Visibility.Visible;
            panel.Opacity = 1;
            panel.Height = double.NaN;
        }

        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SymptomCardViewModel.IsExpanded))
                AnimateExpansion(panel, vm.IsExpanded);
        };
    }

    private static void AnimateExpansion(FrameworkElement panel, bool expand)
    {
        panel.BeginAnimation(FrameworkElement.HeightProperty, null);
        panel.BeginAnimation(UIElement.OpacityProperty, null);

        if (expand)
        {
            panel.Visibility = Visibility.Visible;
            panel.Height = double.NaN;
            panel.Opacity = 1;
            panel.UpdateLayout();
            var targetHeight = panel.ActualHeight;
            panel.Height = 0;
            panel.Opacity = 0;

            var heightAnim = new DoubleAnimation(0, targetHeight, TimeSpan.FromMilliseconds(ExpandMs))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            heightAnim.Completed += (_, _) =>
            {
                // BeginAnimation(..., null) actually releases the property - just assigning
                // Height afterward doesn't, since the animation still holds it (FillBehavior
                // defaults to HoldEnd). Without this, the panel stays stuck at the exact pixel
                // height measured at expand time and never grows when later content (diagnostic
                // results, fix buttons) is added.
                panel.BeginAnimation(FrameworkElement.HeightProperty, null);
                panel.Height = double.NaN;
            };
            panel.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
            panel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ExpandMs)));
        }
        else
        {
            var currentHeight = panel.ActualHeight;
            panel.Height = currentHeight;
            panel.UpdateLayout();

            var heightAnim = new DoubleAnimation(currentHeight, 0, TimeSpan.FromMilliseconds(CollapseMs))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            heightAnim.Completed += (_, _) =>
            {
                panel.Visibility = Visibility.Collapsed;
                panel.BeginAnimation(FrameworkElement.HeightProperty, null);
                panel.Height = double.NaN;
            };
            panel.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
            panel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(CollapseMs)));
        }
    }

    /// <summary>
    /// Fires once for the console panel's whole lifetime (it's static XAML, not per-item like
    /// ExpandPanel_Loaded's cards, so this only runs once per SymptomsView instance). Deliberately
    /// NOT a DataTrigger in XAML - that was tried first and proved unreliable across repeated
    /// expand/collapse/re-expand cycles within the same section (the panel could end up stuck
    /// visible, or visible-but-shrunk, after Back then re-entering a card). This subscribes
    /// directly to SymptomsViewModel.ExpandedCard changes, and re-subscribes to whichever
    /// SymptomCardViewModel is currently expanded for its IsTechConsoleVisible changes, carefully
    /// unsubscribing the previous card's handler each time so cycles don't stack up duplicate
    /// handlers - the same class of bug this whole rewrite exists to avoid.
    /// </summary>
    private void ConsolePanel_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement panel) return;
        if (DataContext is not SymptomsViewModel symptomsVm) return;

        symptomsVm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SymptomsViewModel.ExpandedCard))
                TrackExpandedCard(panel, symptomsVm.ExpandedCard);
        };

        TrackExpandedCard(panel, symptomsVm.ExpandedCard);
    }

    private void TrackExpandedCard(FrameworkElement panel, SymptomCardViewModel? card)
    {
        if (_consoleTrackedCard is not null && _consoleCardHandler is not null)
            _consoleTrackedCard.PropertyChanged -= _consoleCardHandler;

        _consoleTrackedCard = card;

        if (card is not null)
        {
            _consoleCardHandler = (_, args) =>
            {
                if (args.PropertyName == nameof(SymptomCardViewModel.IsTechConsoleVisible))
                    AnimateConsole(panel, card.IsTechConsoleVisible);
            };
            card.PropertyChanged += _consoleCardHandler;
        }
        else
        {
            _consoleCardHandler = null;
        }

        AnimateConsole(panel, card?.IsTechConsoleVisible ?? false);
    }

    private static void AnimateConsole(FrameworkElement panel, bool show)
    {
        panel.BeginAnimation(FrameworkElement.WidthProperty, null);
        panel.BeginAnimation(UIElement.OpacityProperty, null);
        var scale = panel.RenderTransform as System.Windows.Media.ScaleTransform;
        scale?.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        scale?.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);

        if (show)
        {
            panel.Visibility = Visibility.Visible;
            panel.Width = 0;
            panel.Opacity = 0;
            if (scale is not null) { scale.ScaleX = 0.9; scale.ScaleY = 0.9; }

            var widthAnim = new DoubleAnimation(0, 500, TimeSpan.FromMilliseconds(ConsoleShowMs))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            panel.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);
            panel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ConsoleShowMs)));
            scale?.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(ConsoleShowMs)));
            scale?.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(ConsoleShowMs)));
        }
        else
        {
            var widthAnim = new DoubleAnimation(panel.ActualWidth, 0, TimeSpan.FromMilliseconds(ConsoleHideMs));
            widthAnim.Completed += (_, _) =>
            {
                panel.Visibility = Visibility.Collapsed;
                panel.BeginAnimation(FrameworkElement.WidthProperty, null);
                panel.Width = 0;
            };
            panel.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);
            panel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(panel.Opacity, 0, TimeSpan.FromMilliseconds(ConsoleHideMs)));
        }
    }
}
