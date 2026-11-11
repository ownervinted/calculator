using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LemieszeDesktop;

public partial class MainWindow
{
    private int transitionVersion;
    private bool calculatorSelected = true;

    private void Theme_Click(object sender, RoutedEventArgs e) =>
        ThemeManager.Apply(ThemeManager.ReadSavedTheme() == "Light" ? "Dark" : "Light");

    private void SetNavigation(bool calculator)
    {
        CalculatorNav.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty,
            calculator ? "ThemeColor34476E" : "ThemeColor222B41");
        EditorNav.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty,
            calculator ? "ThemeColor222B41" : "ThemeColor34476E");
    }

    private void SwitchPage(bool calculator)
    {
        if (calculatorSelected == calculator) return;
        calculatorSelected = calculator;
        int version = ++transitionVersion;
        SetNavigation(calculator);
        var incoming = calculator ? CalculatorPage : EditorPage;
        var outgoing = calculator ? EditorPage : CalculatorPage;
        incoming.Visibility = Visibility.Visible;
        incoming.IsHitTestVisible = true;
        outgoing.IsHitTestVisible = false;
        System.Windows.Controls.Panel.SetZIndex(incoming, 1);
        System.Windows.Controls.Panel.SetZIndex(outgoing, 0);
        var duration = TimeSpan.FromMilliseconds(320);
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        // Start from current opacity so rapid tab changes never jump or leave a page hidden.
        double from = incoming.Opacity;
        if (from == 1 && outgoing.Opacity == 1) from = 0;
        incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(from, 1, duration) { EasingFunction = easing });
        var fade = new DoubleAnimation(outgoing.Opacity, 0, duration) { EasingFunction = easing };
        fade.Completed += (_, _) =>
        {
            if (version != transitionVersion) return;
            outgoing.Visibility = Visibility.Collapsed;
            outgoing.BeginAnimation(OpacityProperty, null);
            outgoing.Opacity = 0;
            incoming.BeginAnimation(OpacityProperty, null);
            incoming.Opacity = 1;
        };
        outgoing.BeginAnimation(OpacityProperty, fade);
    }
}
