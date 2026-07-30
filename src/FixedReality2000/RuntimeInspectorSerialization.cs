using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FixedReality2000;

internal sealed partial class RuntimeInspectorBridge
{
    private static string ObjectSummary(GameObject gameObject)
    {
        return
            "{" +
            $"\"id\":{gameObject.GetInstanceID()}," +
            $"\"name\":{Json(gameObject.name)}," +
            $"\"path\":{Json(TransformPathUtility.GetPath(gameObject.transform))}," +
            $"\"activeSelf\":{Bool(gameObject.activeSelf)}," +
            $"\"activeInHierarchy\":{Bool(gameObject.activeInHierarchy)}," +
            $"\"layer\":{gameObject.layer}," +
            $"\"scene\":{Json(gameObject.scene.name)}" +
            "}";
    }

    private static string ComponentDetails(Component component)
    {
        var properties = new List<string>
        {
            $"\"type\":{Json(component.GetType().FullName ?? component.GetType().Name)}",
            $"\"id\":{component.GetInstanceID()}"
        };
        if (component is Behaviour behaviour)
        {
            properties.Add($"\"enabled\":{Bool(behaviour.enabled)}");
        }

        if (component is RectTransform rect)
        {
            properties.Add(
                $"\"parent\":{Json(rect.parent != null ? TransformPathUtility.GetPath(rect.parent) : string.Empty)}");
            properties.Add($"\"anchoredPosition\":{Vector(rect.anchoredPosition)}");
            properties.Add($"\"sizeDelta\":{Vector(rect.sizeDelta)}");
            properties.Add($"\"anchorMin\":{Vector(rect.anchorMin)}");
            properties.Add($"\"anchorMax\":{Vector(rect.anchorMax)}");
            properties.Add($"\"pivot\":{Vector(rect.pivot)}");
            properties.Add($"\"localScale\":{Vector(rect.localScale)}");
            properties.Add($"\"lossyScale\":{Vector(rect.lossyScale)}");
            properties.Add($"\"rect\":{RectValue(rect.rect)}");
            properties.Add($"\"screenRect\":{RectValue(GetScreenRect(rect))}");
        }

        if (component is TMP_Text text)
        {
            properties.Add($"\"text\":{Json(text.text)}");
            properties.Add($"\"font\":{Json(text.font != null ? text.font.name : string.Empty)}");
            properties.Add(
                $"\"material\":{Json(text.fontSharedMaterial != null ? text.fontSharedMaterial.name : string.Empty)}");
            properties.Add($"\"fontSize\":{Number(text.fontSize)}");
            properties.Add($"\"fontStyle\":{Json(text.fontStyle.ToString())}");
            properties.Add($"\"alignment\":{Json(text.alignment.ToString())}");
            properties.Add($"\"color\":{ColorValue(text.color)}");
            properties.Add($"\"maskable\":{Bool(text.maskable)}");
            properties.Add($"\"culled\":{Bool(text.canvasRenderer.cull)}");
        }

        if (component is Canvas canvas)
        {
            properties.Add($"\"renderMode\":{Json(canvas.renderMode.ToString())}");
            properties.Add($"\"sortingOrder\":{canvas.sortingOrder}");
            properties.Add($"\"overrideSorting\":{Bool(canvas.overrideSorting)}");
            properties.Add($"\"scaleFactor\":{Number(canvas.scaleFactor)}");
            properties.Add($"\"isRootCanvas\":{Bool(canvas.isRootCanvas)}");
        }

        if (component is Slider slider)
        {
            properties.Add($"\"value\":{Number(slider.value)}");
            properties.Add($"\"minValue\":{Number(slider.minValue)}");
            properties.Add($"\"maxValue\":{Number(slider.maxValue)}");
            properties.Add(
                $"\"handle\":{Json(slider.handleRect != null ? TransformPathUtility.GetPath(slider.handleRect) : string.Empty)}");
        }

        if (component is TMP_Dropdown dropdown)
        {
            properties.Add($"\"value\":{dropdown.value}");
            properties.Add(
                $"\"caption\":{Json(dropdown.captionText != null ? dropdown.captionText.text : string.Empty)}");
            properties.Add(
                "\"options\":[" +
                string.Join(",", dropdown.options.Select(option => Json(option.text))) +
                "]");
        }

        if (component is CanvasRenderer renderer)
        {
            properties.Add($"\"cull\":{Bool(renderer.cull)}");
            properties.Add($"\"cullTransparentMesh\":{Bool(renderer.cullTransparentMesh)}");
        }

        return "{" + string.Join(",", properties) + "}";
    }

    private static string TransformTree(Transform transform, int depth, int maxChildren)
    {
        var builder = new StringBuilder();
        builder.Append('{');
        builder.Append($"\"id\":{transform.gameObject.GetInstanceID()},");
        builder.Append($"\"name\":{Json(transform.name)},");
        builder.Append($"\"active\":{Bool(transform.gameObject.activeInHierarchy)},");
        builder.Append("\"components\":[");
        builder.Append(
            string.Join(
                ",",
                transform.gameObject.GetComponents<Component>()
                    .Where(component => component != null)
                    .Select(component => Json(component.GetType().Name))));
        builder.Append(']');
        if (depth > 0 && transform.childCount > 0)
        {
            builder.Append(",\"children\":[");
            int count = Mathf.Min(transform.childCount, maxChildren);
            for (int index = 0; index < count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                builder.Append(TransformTree(transform.GetChild(index), depth - 1, maxChildren));
            }

            builder.Append(']');
        }

        builder.Append('}');
        return builder.ToString();
    }

    private static Rect GetScreenRect(RectTransform rectTransform)
    {
        Canvas? rootCanvas = rectTransform.GetComponentInParent<Canvas>()?.rootCanvas;
        Camera? camera =
            rootCanvas == null || rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : rootCanvas.worldCamera;
        var corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);
        Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
        Vector2 topRight = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        return Rect.MinMaxRect(bottomLeft.x, bottomLeft.y, topRight.x, topRight.y);
    }

    private static string Get(IDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out string? value) ? value : string.Empty;
    }

    private static int GetInt(
        IDictionary<string, string> values,
        string key,
        int fallback)
    {
        return int.TryParse(
            Get(values, key),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int value)
                ? value
                : fallback;
    }

    private static bool GetBool(
        IDictionary<string, string> values,
        string key,
        bool fallback)
    {
        string value = Get(values, key);
        return string.IsNullOrEmpty(value) ? fallback : ParseBool(value);
    }

    private static bool ParseBool(string value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static float ParseFloat(string value)
    {
        return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static Vector2 ParseVector2(string value)
    {
        string[] parts = value.Split(',');
        if (parts.Length != 2)
        {
            throw new FormatException("A Vector2 value must use the form 'x,y'.");
        }

        return new Vector2(ParseFloat(parts[0]), ParseFloat(parts[1]));
    }

    private static string Json(string? value)
    {
        if (value == null)
        {
            return "null";
        }

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (char character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (character < 32)
                    {
                        builder.Append("\\u");
                        builder.Append(((int)character).ToString("x4"));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static string Bool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string Number(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string Vector(Vector2 value)
    {
        return $"{{\"x\":{Number(value.x)},\"y\":{Number(value.y)}}}";
    }

    private static string Vector(Vector3 value)
    {
        return
            $"{{\"x\":{Number(value.x)},\"y\":{Number(value.y)},\"z\":{Number(value.z)}}}";
    }

    private static string RectValue(Rect value)
    {
        return
            $"{{\"x\":{Number(value.x)},\"y\":{Number(value.y)}," +
            $"\"width\":{Number(value.width)},\"height\":{Number(value.height)}}}";
    }

    private static string ColorValue(Color value)
    {
        return
            $"{{\"r\":{Number(value.r)},\"g\":{Number(value.g)}," +
            $"\"b\":{Number(value.b)},\"a\":{Number(value.a)}}}";
    }

}
