using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace Ooshak;

public readonly record struct LiquidFrame(double Level, Color Color);

/// <summary>
/// A pool of liquid that sloshes with the voice: a spring-connected surface, an idle swell,
/// and bubbles that rise with loudness and kick the surface when they pop.
/// </summary>
public sealed class LiquidView : FrameworkElement
{
    private const int Columns = 34;
    private const double Tension = 280;
    private const double Restoring = 8;
    private const double Damping = 2.8;
    private const double MaxDisplacement = 0.22;
    private const double ImpulseStrength = 4.2;
    private const double PopStrength = 0.75;
    private const double BubbleRate = 26;
    private const double MaxStep = 1.0 / 30;
    private const double MinFrameSeconds = 1.0 / 90;

    private readonly double[] _height = new double[Columns];
    private readonly double[] _velocity = new double[Columns];
    private readonly Point[] _front = new Point[Columns];
    private readonly Point[] _back = new Point[Columns];
    private readonly List<Bubble> _bubbles = [];
    private readonly Random _random = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly bool _reducedMotion = !SystemParameters.ClientAreaAnimation;
    private double _lastFrame;
    private double _time;
    private double _level;
    private double _fill = 0.25;
    private double _bubbleCredit;
    private Color _color = Colors.Gray;
    private Brush? _frontBrush;
    private Brush? _backBrush;
    private Pen? _surfacePen;
    private Color _brushColor;
    private bool _subscribed;

    public LiquidView()
    {
        IsVisibleChanged += (_, _) => UpdateSubscription();
        Unloaded += (_, _) => UpdateSubscription(forceOff: true);
        Loaded += (_, _) => UpdateSubscription();
    }

    public Func<LiquidFrame>? Source { get; set; }
    public double CornerRadius { get; set; }
    public double MinFill { get; set; } = 0.22;
    public double MaxFill { get; set; } = 0.8;

    private void UpdateSubscription(bool forceOff = false)
    {
        var want = !forceOff && IsVisible;
        if (want == _subscribed)
            return;

        _subscribed = want;
        if (want)
            CompositionTarget.Rendering += OnFrame;
        else
            CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var elapsed = now - _lastFrame;
        if (elapsed < MinFrameSeconds)
            return;

        _lastFrame = now;
        Advance(Math.Min(elapsed, MaxStep));
        InvalidateVisual();
    }

    private void Advance(double dt)
    {
        _time += dt;

        var frame = Source?.Invoke() ?? new LiquidFrame(0, Colors.Gray);
        _level += (frame.Level - _level) * (1 - Math.Exp(-dt * 16));
        _fill += (MinFill + _level * (MaxFill - MinFill) - _fill) * (1 - Math.Exp(-dt * 7));
        _color = Blend(_color, frame.Color, 1 - Math.Exp(-dt * 8));

        if (_reducedMotion)
            return;

        Stir(dt);
        StepSurface(dt);
        StepBubbles(dt);
    }

    private void Stir(double dt)
    {
        var strength = _level * _level * ImpulseStrength;
        if (strength < 0.01)
            return;

        for (var n = 0; n < 3; n++)
            _velocity[_random.Next(Columns)] += (_random.NextDouble() - 0.5) * strength * dt * 60;
    }

    private void StepSurface(double dt)
    {
        const int substeps = 2;
        var step = dt / substeps;
        var damping = Math.Exp(-Damping * step);

        for (var s = 0; s < substeps; s++)
        {
            for (var i = 0; i < Columns; i++)
            {
                var left = _height[Math.Max(i - 1, 0)];
                var right = _height[Math.Min(i + 1, Columns - 1)];
                var acceleration = Tension * (left + right - 2 * _height[i]) - Restoring * _height[i];
                _velocity[i] = (_velocity[i] + acceleration * step) * damping;
            }

            for (var i = 0; i < Columns; i++)
                _height[i] = Math.Clamp(_height[i] + _velocity[i] * step, -MaxDisplacement, MaxDisplacement);
        }
    }

    private void StepBubbles(double dt)
    {
        _bubbleCredit += dt * _level * _level * BubbleRate;
        while (_bubbleCredit >= 1)
        {
            _bubbleCredit -= 1;
            _bubbles.Add(new Bubble
            {
                X = _random.NextDouble(),
                Depth = 1.02,
                Radius = 1.5 + _random.NextDouble() * 3.5,
                Speed = 0.35 + _random.NextDouble() * 0.6,
                Phase = _random.NextDouble() * Math.PI * 2,
            });
        }

        for (var i = _bubbles.Count - 1; i >= 0; i--)
        {
            var bubble = _bubbles[i];
            bubble.Depth -= bubble.Speed * dt;

            if (bubble.Depth > 1 - _fill)
                continue;

            var column = Math.Clamp((int)(bubble.X * (Columns - 1)), 0, Columns - 1);
            _velocity[column] += PopStrength;
            _bubbles.RemoveAt(i);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        EnsureBrushes();
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height), CornerRadius, CornerRadius));

        for (var i = 0; i < Columns; i++)
        {
            var x = width * i / (Columns - 1);
            var swell = Math.Sin(i * 0.33 + _time * 1.6) * 0.012 + Math.Sin(i * 0.11 - _time * 0.9) * 0.012;
            var y = height * (1 - _fill - _height[i] - swell);
            _front[i] = new Point(x, y);
            _back[i] = new Point(x, y - height * (0.035 + 0.02 * Math.Sin(i * 0.42 - _time * 2.1)));
        }

        dc.DrawGeometry(_backBrush, null, Surface(_back, width, height, closed: true));
        dc.DrawGeometry(_frontBrush, null, Surface(_front, width, height, closed: true));
        dc.DrawGeometry(null, _surfacePen, Surface(_front, width, height, closed: false));

        foreach (var bubble in _bubbles)
        {
            var x = (bubble.X + Math.Sin(_time * 5 + bubble.Phase) * 0.012) * width;
            dc.DrawEllipse(BubbleFill, BubbleEdge, new Point(x, bubble.Depth * height), bubble.Radius, bubble.Radius);
        }

        dc.Pop();
    }

    private static StreamGeometry Surface(Point[] points, double width, double height, bool closed)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(closed ? new Point(0, height) : points[0], closed, closed);
            if (closed)
                context.LineTo(points[0], false, false);

            for (var i = 1; i < points.Length - 1; i++)
            {
                var mid = new Point((points[i].X + points[i + 1].X) / 2, (points[i].Y + points[i + 1].Y) / 2);
                context.QuadraticBezierTo(points[i], mid, true, true);
            }

            context.LineTo(points[^1], true, true);
            if (closed)
                context.LineTo(new Point(width, height), false, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private void EnsureBrushes()
    {
        if (_frontBrush is not null && _brushColor == _color)
            return;

        _brushColor = _color;

        var top = Blend(_color, Colors.White, 0.12);
        var bottom = Blend(_color, Colors.Black, 0.42);
        var front = new LinearGradientBrush(top, bottom, 90);
        front.Freeze();
        _frontBrush = front;

        var back = new SolidColorBrush(Color.FromArgb(90, _color.R, _color.G, _color.B));
        back.Freeze();
        _backBrush = back;

        var surface = new SolidColorBrush(Blend(_color, Colors.White, 0.5));
        surface.Freeze();
        _surfacePen = new Pen(surface, 1.2);
        _surfacePen.Freeze();
    }

    private static Color Blend(Color from, Color to, double amount) => Color.FromRgb(
        (byte)(from.R + (to.R - from.R) * amount),
        (byte)(from.G + (to.G - from.G) * amount),
        (byte)(from.B + (to.B - from.B) * amount));

    private static readonly Brush BubbleFill = Frozen(Color.FromArgb(46, 255, 255, 255));
    private static readonly Pen BubbleEdge = FrozenPen(Color.FromArgb(110, 255, 255, 255));

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color)
    {
        var pen = new Pen(Frozen(color), 0.8);
        pen.Freeze();
        return pen;
    }

    private sealed class Bubble
    {
        public double X { get; init; }
        public double Depth { get; set; }
        public double Radius { get; init; }
        public double Speed { get; init; }
        public double Phase { get; init; }
    }
}
