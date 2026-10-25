using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Chango.UI;

/// <summary>
/// The launch animation: a frosted card in the middle of the screen where the logo assembles itself (the Lottie
/// intro), then "Chango", what it is and the tagline rise in, and the card fades away. About two seconds; never takes focus; a
/// click dismisses it. Reduced motion shows the finished logo briefly instead.
/// </summary>
public sealed class SplashWindow : Window
{
    private readonly LottieView _logo;
    private readonly StackPanel _words;
    private readonly TranslateTransform _rise = new(0, 10);
    private bool _closing;

    public SplashWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = "Chango";
        UseLayoutRounding = true;

        _logo = new LottieView
        {
            AnimationSource = "pack://application:,,,/Chango.UI;component/Assets/chango-intro.json",
            FallbackSource = "pack://application:,,,/Chango.UI;component/Assets/chango-mark-256.png",
            Width = 168, Height = 168, PlayOnLoad = false, HorizontalAlignment = HorizontalAlignment.Center,
        };
        _logo.Completed += OnLogoDone;

        var name = new TextBlock { Text = "Chango", FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center };
        name.SetResourceReference(StyleProperty, "Chango.Text.Title");
        var subtitle = new TextBlock { Text = Infrastructure.Brand.Subtitle, TextWrapping = TextWrapping.NoWrap, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 6) };
        subtitle.SetResourceReference(StyleProperty, "Chango.Text.Body");
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "Chango.TextSecondary");
        var tagline = new TextBlock { Text = Infrastructure.Brand.Tagline, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        tagline.SetResourceReference(StyleProperty, "Chango.Text.Body");
        tagline.SetResourceReference(TextBlock.ForegroundProperty, "Chango.AccentText");
        _words = new StackPanel { Opacity = 0, RenderTransform = _rise, Margin = new Thickness(0, 4, 0, 0) };
        _words.Children.Add(name);
        _words.Children.Add(subtitle);
        _words.Children.Add(tagline);

        var stack = new StackPanel { Margin = new Thickness(40, 28, 40, 30) };
        stack.Children.Add(_logo);
        stack.Children.Add(_words);

        var card = new Border
        {
            CornerRadius = new CornerRadius(28), BorderThickness = new Thickness(1), Child = stack, Margin = new Thickness(24),
            Effect = new DropShadowEffect { BlurRadius = 36, ShadowDepth = 10, Direction = 270, Opacity = 0.22 },
        };
        card.SetResourceReference(Border.BackgroundProperty, "Chango.Ambient");
        card.SetResourceReference(Border.BorderBrushProperty, "Chango.GlassStroke");
        Content = card;

        MouseLeftButtonDown += (_, _) => FadeOut();
        ContentRendered += (_, _) => _logo.Play();
    }

    /// <summary>The finished card, for offscreen design-review renders.</summary>
    public FrameworkElement ShowStill()
    {
        _logo.ShowFrame(1);
        _words.Opacity = 1;
        _rise.Y = 0;
        return (FrameworkElement)Content;
    }

    private void OnLogoDone()
    {
        bool still = ThemeManager.Current.ReduceMotion;
        var appear = TimeSpan.FromMilliseconds(still ? 0 : 280);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _words.BeginAnimation(OpacityProperty, new DoubleAnimation(1, appear) { EasingFunction = ease });
        _rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, appear) { EasingFunction = ease });
        var hold = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(still ? 1200 : 750) };
        hold.Tick += (_, _) => { hold.Stop(); FadeOut(); };
        hold.Start();
    }

    private void FadeOut()
    {
        if (_closing) return;
        _closing = true;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(ThemeManager.Current.ReduceMotion ? 0 : 260));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
