using System.Collections.Generic;
using UnityEngine;

namespace FixedReality2000;

internal static class TransformPathUtility
{
    internal static string GetPath(Transform transform)
    {
        var parts = new Stack<string>();
        Transform? current = transform;
        while (current != null)
        {
            parts.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", parts);
    }
}
