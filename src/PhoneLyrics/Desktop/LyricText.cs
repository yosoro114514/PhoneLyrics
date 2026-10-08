using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Automation.Peers;

namespace PhoneLyrics;

// Glyph geometry gives a real stroke around each character, rather than a rectangular caption background.
internal sealed class LyricText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(LyricText),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(LyricText),
        new FrameworkPropertyMetadata(46d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(LyricText),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(nameof(Compact), typeof(bool), typeof(LyricText),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public bool Compact { get => (bool)GetValue(CompactProperty); set => SetValue(CompactProperty, value); }

    public LyricText()
    {
        Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 7, ShadowDepth = 2, Opacity = .8 };
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (string.IsNullOrEmpty(Text) || ActualWidth < 30 || ActualHeight < (Compact ? 10 : 20)) return;
        var inset = Compact ? 6 : 15;
        var available = ActualWidth - inset * 2;
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var face = new Typeface(new FontFamily("Microsoft YaHei UI, Yu Gothic UI, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        FormattedText Format(double size) => new(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, Fill, pixelsPerDip);
        var formatted = Format(FontSize);
        if (formatted.WidthIncludingTrailingWhitespace > available)
            formatted = Format(Math.Max(Compact ? 11 : 20, FontSize * available / formatted.WidthIncludingTrailingWhitespace));
        formatted.MaxTextWidth = available;
        formatted.MaxLineCount = 1;
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        formatted.TextAlignment = TextAlignment.Center;
        var geometry = formatted.BuildGeometry(new Point(inset, Math.Max(0, (ActualHeight - formatted.Height) / 2)));
        var outline = new Pen(new SolidColorBrush(Color.FromArgb(235, 20, 16, 13)), Math.Max(Compact ? 1 : 2.2, formatted.Height / (Compact ? 24 : 17)))
            { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        drawing.DrawGeometry(null, outline, geometry);
        drawing.DrawGeometry(Fill, null, geometry);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LyricPeer(this);
    private sealed class LyricPeer(LyricText owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetNameCore() => owner.Text;
        protected override string GetClassNameCore() => nameof(LyricText);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
    }
}
