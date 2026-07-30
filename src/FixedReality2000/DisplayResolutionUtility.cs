using System.Collections.Generic;
using UnityEngine;

namespace FixedReality2000;

internal static class DisplayResolutionUtility
{
    internal static void AddMode(
        IDictionary<(int Width, int Height), HashSet<int>> modes,
        int width,
        int height,
        int refreshRate)
    {
        if (width < 640 || height < 480)
        {
            return;
        }

        var key = (width, height);
        if (!modes.TryGetValue(key, out HashSet<int>? rates))
        {
            rates = new HashSet<int>();
            modes.Add(key, rates);
        }

        if (refreshRate > 0)
        {
            rates.Add(refreshRate);
        }
    }

    internal static int GetRefreshRate(Resolution resolution)
    {
        return Mathf.Max(
            1,
            Mathf.RoundToInt((float)resolution.refreshRateRatio.value));
    }

    internal static int CompareBySizeDescending(
        DisplayResolutionChoice left,
        DisplayResolutionChoice right)
    {
        long leftPixels = (long)left.Width * left.Height;
        long rightPixels = (long)right.Width * right.Height;
        int pixels = rightPixels.CompareTo(leftPixels);
        return pixels != 0
            ? pixels
            : right.Width.CompareTo(left.Width);
    }
}

internal sealed class DisplayResolutionChoice
{
    internal DisplayResolutionChoice(
        int width,
        int height,
        int[] refreshRates)
    {
        Width = width;
        Height = height;
        RefreshRates = refreshRates;
    }

    internal int Width { get; }

    internal int Height { get; }

    internal int[] RefreshRates { get; }
}
