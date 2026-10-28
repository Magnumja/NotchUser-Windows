using System.Windows;
using System.Windows.Media.Animation;

namespace NotchUser;

/// <summary>
/// The same springs as SwiftUI's .spring(response:dampingFraction:) on the Mac version:
/// a damped oscillation, so things settle with a little overshoot instead of a fixed curve.
/// </summary>
public sealed class SpringEase : IEasingFunction
{
    private readonly double _w0, _zeta, _wd;
    public TimeSpan Duration { get; }

    public SpringEase(double response, double damping)
    {
        _w0 = 2 * Math.PI / response;
        _zeta = damping;
        _wd = _w0 * Math.Sqrt(1 - damping * damping);
        // Until the wobble is under 0.1% of the distance.
        Duration = TimeSpan.FromSeconds(Math.Min(Math.Log(1000) / (damping * _w0), 1.5));
    }

    public double Ease(double progress)
    {
        if (progress >= 1) return 1;
        var t = progress * Duration.TotalSeconds;
        var decay = Math.Exp(-_zeta * _w0 * t);
        return 1 - decay * (Math.Cos(_wd * t) + _zeta * _w0 / _wd * Math.Sin(_wd * t));
    }

    /// <summary>A 60 fps animation to <paramref name="to"/> riding this spring.</summary>
    public static DoubleAnimation To(double to, double response, double damping, double delay = 0, double? from = null)
    {
        var ease = new SpringEase(response, damping);
        var a = new DoubleAnimation(to, ease.Duration) { EasingFunction = ease, BeginTime = TimeSpan.FromSeconds(delay) };
        if (from is double f) a.From = f;
        Timeline.SetDesiredFrameRate(a, 60);
        return a;
    }
}
