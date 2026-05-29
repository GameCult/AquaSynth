namespace AquaSynth.Dsl;

public sealed class ControlSplineTimeline
{
    private readonly Dictionary<string, List<ControlSplinePoint>> _pointsBySurface;

    public ControlSplineTimeline(IEnumerable<ControlSpline> splines)
    {
        _pointsBySurface = splines
            .Where(spline => spline.Enabled)
            .GroupBy(spline => spline.SurfacePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(spline => spline.Points)
                    .OrderBy(point => point.TimeSeconds)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> SurfacePaths => _pointsBySurface.Keys.ToArray();

    public void SetFuturePoint(string surfacePath, ControlSplinePoint point, float nowSeconds)
    {
        if (point.TimeSeconds < nowSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(point), "future spline edits must not rewrite past control points");
        }

        if (!_pointsBySurface.TryGetValue(surfacePath, out var points))
        {
            points = [];
            _pointsBySurface[surfacePath] = points;
        }

        var existing = points.FindIndex(candidate => MathF.Abs(candidate.TimeSeconds - point.TimeSeconds) <= 0.000001f);
        if (existing >= 0)
        {
            points[existing] = point;
        }
        else
        {
            points.Add(point);
        }

        points.Sort((left, right) => left.TimeSeconds.CompareTo(right.TimeSeconds));
    }

    public float ValueAt(string surfacePath, float timeSeconds, float fallback = 0)
    {
        if (!_pointsBySurface.TryGetValue(surfacePath, out var points) || points.Count == 0)
        {
            return fallback;
        }

        return Evaluate(points, timeSeconds);
    }

    public static float Evaluate(ControlSpline spline, float timeSeconds, float fallback = 0)
    {
        if (!spline.Enabled || spline.Points.Count == 0)
        {
            return fallback;
        }

        var ordered = spline.Points.OrderBy(point => point.TimeSeconds).ToArray();
        if (spline.Loop && ordered.Length > 1)
        {
            var duration = MathF.Max(0.000001f, ordered[^1].TimeSeconds - ordered[0].TimeSeconds);
            timeSeconds = ordered[0].TimeSeconds + PositiveModulo(timeSeconds - ordered[0].TimeSeconds, duration);
        }

        return spline.Interpolation switch
        {
            ControlSplineInterpolation.Hold => EvaluateHold(ordered, timeSeconds),
            ControlSplineInterpolation.Linear => EvaluateLinear(ordered, timeSeconds),
            _ => EvaluateBezier(ordered, timeSeconds)
        };
    }

    private static float Evaluate(IReadOnlyList<ControlSplinePoint> points, float timeSeconds)
    {
        if (points.Count == 1)
        {
            return Math.Clamp(points[0].Value, 0, 1);
        }

        return EvaluateBezier(points, timeSeconds);
    }

    private static float EvaluateHold(IReadOnlyList<ControlSplinePoint> points, float timeSeconds)
    {
        var current = points[0];
        foreach (var point in points)
        {
            if (point.TimeSeconds > timeSeconds)
            {
                break;
            }

            current = point;
        }

        return Math.Clamp(current.Value, 0, 1);
    }

    private static float EvaluateLinear(IReadOnlyList<ControlSplinePoint> points, float timeSeconds)
    {
        if (timeSeconds <= points[0].TimeSeconds) return Math.Clamp(points[0].Value, 0, 1);
        for (var i = 0; i < points.Count - 1; i++)
        {
            var from = points[i];
            var to = points[i + 1];
            if (timeSeconds > to.TimeSeconds)
            {
                continue;
            }

            var segmentDuration = MathF.Max(0.000001f, to.TimeSeconds - from.TimeSeconds);
            var t = Math.Clamp((timeSeconds - from.TimeSeconds) / segmentDuration, 0, 1);
            return Math.Clamp(from.Value + (to.Value - from.Value) * t, 0, 1);
        }

        return Math.Clamp(points[^1].Value, 0, 1);
    }

    private static float EvaluateBezier(IReadOnlyList<ControlSplinePoint> points, float timeSeconds)
    {
        if (timeSeconds <= points[0].TimeSeconds) return Math.Clamp(points[0].Value, 0, 1);
        for (var i = 0; i < points.Count - 1; i++)
        {
            var from = points[i];
            var to = points[i + 1];
            if (timeSeconds > to.TimeSeconds)
            {
                continue;
            }

            var t = SolveBezierTime(from, to, timeSeconds);
            var value = Cubic(
                from.Value,
                from.OutValue == 0 ? from.Value : from.OutValue,
                to.InValue == 0 ? to.Value : to.InValue,
                to.Value,
                t);
            return Math.Clamp(value, 0, 1);
        }

        return Math.Clamp(points[^1].Value, 0, 1);
    }

    private static float SolveBezierTime(ControlSplinePoint from, ControlSplinePoint to, float timeSeconds)
    {
        var c0 = from.TimeSeconds;
        var c1 = from.TimeSeconds + MathF.Max(0, from.OutTimeOffsetSeconds);
        var c2 = to.TimeSeconds - MathF.Max(0, to.InTimeOffsetSeconds);
        var c3 = to.TimeSeconds;
        var low = 0f;
        var high = 1f;
        var t = Math.Clamp((timeSeconds - from.TimeSeconds) / MathF.Max(0.000001f, to.TimeSeconds - from.TimeSeconds), 0, 1);
        for (var i = 0; i < 10; i++)
        {
            var estimate = Cubic(c0, c1, c2, c3, t);
            if (estimate < timeSeconds)
            {
                low = t;
            }
            else
            {
                high = t;
            }

            t = (low + high) * 0.5f;
        }

        return t;
    }

    private static float Cubic(float p0, float p1, float p2, float p3, float t)
    {
        var u = 1 - t;
        return u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3;
    }

    private static float PositiveModulo(float value, float modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }
}
