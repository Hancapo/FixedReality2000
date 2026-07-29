using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FixedReality2000;

internal sealed class RuntimeInspectorBridge : IDisposable
{
    private const int Port = 53987;
    private readonly ConcurrentQueue<PendingRequest> _requests = new();
    private TcpListener? _listener;
    private Thread? _listenerThread;
    private volatile bool _running;

    internal void Start()
    {
        if (_running)
        {
            return;
        }

        try
        {
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
            _running = true;
            _listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "FixedReality2000 Runtime Inspector"
            };
            _listenerThread.Start();
            Plugin.Log.LogInfo(
                $"UnityExplorer MCP runtime bridge listening on 127.0.0.1:{Port}.");
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(
                $"Could not start the UnityExplorer MCP runtime bridge: {exception}");
            Dispose();
        }
    }

    internal void Tick()
    {
        int processed = 0;
        while (processed < 16 && _requests.TryDequeue(out PendingRequest? request))
        {
            try
            {
                request.Response = Execute(request.Path, request.Query);
                request.StatusCode = 200;
            }
            catch (Exception exception)
            {
                request.Response =
                    $"{{\"ok\":false,\"error\":{Json(exception.ToString())}}}";
                request.StatusCode = 500;
            }
            finally
            {
                request.Completed.Set();
            }

            processed++;
        }
    }

    public void Dispose()
    {
        _running = false;
        try
        {
            _listener?.Stop();
        }
        catch
        {
            // The listener may already be stopped during game shutdown.
        }

        while (_requests.TryDequeue(out PendingRequest? request))
        {
            request.Response =
                "{\"ok\":false,\"error\":\"The game bridge is shutting down.\"}";
            request.StatusCode = 503;
            request.Completed.Set();
        }

        _listener = null;
        _listenerThread = null;
    }

    private void ListenLoop()
    {
        while (_running && _listener != null)
        {
            try
            {
                TcpClient client = _listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
            }
            catch (SocketException)
            {
                if (_running)
                {
                    Plugin.Log.LogWarning("The runtime inspector listener stopped unexpectedly.");
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning($"Runtime inspector listener error: {exception.Message}");
            }
        }
    }

    private void HandleClient(TcpClient client)
    {
        using (client)
        using (NetworkStream stream = client.GetStream())
        using (var reader = new StreamReader(
                   stream,
                   Encoding.ASCII,
                   detectEncodingFromByteOrderMarks: false,
                   bufferSize: 4096,
                   leaveOpen: true))
        {
            try
            {
                client.ReceiveTimeout = 12000;
                client.SendTimeout = 12000;
                string? requestLine = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(requestLine))
                {
                    return;
                }

                string[] parts = requestLine.Split(' ');
                if (parts.Length < 2 || !string.Equals(parts[0], "GET", StringComparison.Ordinal))
                {
                    WriteResponse(stream, 405, "{\"ok\":false,\"error\":\"GET only\"}");
                    return;
                }

                string? header;
                do
                {
                    header = reader.ReadLine();
                }
                while (!string.IsNullOrEmpty(header));

                ParseTarget(parts[1], out string path, out Dictionary<string, string> query);
                var pending = new PendingRequest(path, query);
                _requests.Enqueue(pending);
                if (!pending.Completed.WaitOne(10000))
                {
                    WriteResponse(
                        stream,
                        504,
                        "{\"ok\":false,\"error\":\"The Unity main thread did not answer in time.\"}");
                    return;
                }

                WriteResponse(stream, pending.StatusCode, pending.Response);
            }
            catch (Exception exception)
            {
                try
                {
                    WriteResponse(
                        stream,
                        500,
                        $"{{\"ok\":false,\"error\":{Json(exception.Message)}}}");
                }
                catch
                {
                    // The client disconnected.
                }
            }
        }
    }

    private static void WriteResponse(NetworkStream stream, int statusCode, string body)
    {
        byte[] payload = Encoding.UTF8.GetBytes(body);
        string status = statusCode == 200 ? "OK" : "Error";
        byte[] headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} {status}\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {payload.Length}\r\n" +
            "Connection: close\r\n" +
            "Access-Control-Allow-Origin: *\r\n\r\n");
        stream.Write(headers, 0, headers.Length);
        stream.Write(payload, 0, payload.Length);
        stream.Flush();
    }

    private static void ParseTarget(
        string target,
        out string path,
        out Dictionary<string, string> query)
    {
        int separator = target.IndexOf('?');
        path = separator >= 0 ? target.Substring(0, separator) : target;
        query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (separator < 0 || separator + 1 >= target.Length)
        {
            return;
        }

        foreach (string pair in target.Substring(separator + 1).Split('&'))
        {
            if (string.IsNullOrEmpty(pair))
            {
                continue;
            }

            int equals = pair.IndexOf('=');
            string key = equals >= 0 ? pair.Substring(0, equals) : pair;
            string value = equals >= 0 ? pair.Substring(equals + 1) : string.Empty;
            query[Decode(key)] = Decode(value);
        }
    }

    private static string Decode(string value)
    {
        return Uri.UnescapeDataString(value.Replace('+', ' '));
    }

    private static string Execute(string path, IDictionary<string, string> query)
    {
        switch (path.TrimEnd('/').ToLowerInvariant())
        {
            case "":
            case "/ping":
                return Ping();
            case "/find":
                return Find(query);
            case "/inspect":
                return Inspect(query);
            case "/hierarchy":
                return Hierarchy(query);
            case "/ue-inspect":
                return OpenInUnityExplorer(query);
            case "/set":
                return SetProperty(query);
            default:
                return "{\"ok\":false,\"error\":\"Unknown endpoint.\"}";
        }
    }

    private static string Ping()
    {
        bool unityExplorerLoaded =
            AppDomain.CurrentDomain.GetAssemblies().Any(
                assembly => assembly.GetName().Name?.IndexOf(
                    "UnityExplorer",
                    StringComparison.OrdinalIgnoreCase) >= 0);
        return
            "{\"ok\":true," +
            $"\"unityVersion\":{Json(Application.unityVersion)}," +
            $"\"scene\":{Json(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)}," +
            $"\"unityExplorerLoaded\":{Bool(unityExplorerLoaded)}," +
            "\"bridgeVersion\":\"0.1.0\"}";
    }

    private static string Find(IDictionary<string, string> query)
    {
        string name = Get(query, "name");
        string component = Get(query, "component");
        bool includeInactive = GetBool(query, "includeInactive", true);
        int limit = Mathf.Clamp(GetInt(query, "limit", 50), 1, 500);

        IEnumerable<GameObject> objects =
            Resources.FindObjectsOfTypeAll<GameObject>()
                .Where(gameObject =>
                    gameObject != null &&
                    gameObject.scene.IsValid() &&
                    (includeInactive || gameObject.activeInHierarchy));
        if (!string.IsNullOrWhiteSpace(name))
        {
            objects = objects.Where(
                gameObject =>
                    gameObject.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    BuildPath(gameObject.transform).IndexOf(
                        name,
                        StringComparison.OrdinalIgnoreCase) >= 0);
        }

        if (!string.IsNullOrWhiteSpace(component))
        {
            objects = objects.Where(
                gameObject => gameObject
                    .GetComponents<Component>()
                    .Any(value =>
                        value != null &&
                        (value.GetType().Name.IndexOf(
                             component,
                             StringComparison.OrdinalIgnoreCase) >= 0 ||
                         (value.GetType().FullName?.IndexOf(
                              component,
                              StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)));
        }

        GameObject[] results = objects
            .OrderBy(gameObject => BuildPath(gameObject.transform))
            .Take(limit)
            .ToArray();
        return
            "{\"ok\":true," +
            $"\"count\":{results.Length}," +
            "\"objects\":[" +
            string.Join(",", results.Select(ObjectSummary)) +
            "]}";
    }

    private static string Inspect(IDictionary<string, string> query)
    {
        GameObject? gameObject = ResolveGameObject(query);
        if (gameObject == null)
        {
            return "{\"ok\":false,\"error\":\"Object not found.\"}";
        }

        Component[] components = gameObject.GetComponents<Component>();
        var builder = new StringBuilder();
        builder.Append("{\"ok\":true,\"object\":");
        builder.Append(ObjectSummary(gameObject));
        builder.Append(",\"components\":[");
        bool first = true;
        foreach (Component component in components)
        {
            if (component == null)
            {
                continue;
            }

            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            builder.Append(ComponentDetails(component));
        }

        builder.Append("]}");
        return builder.ToString();
    }

    private static string Hierarchy(IDictionary<string, string> query)
    {
        GameObject? gameObject = ResolveGameObject(query);
        if (gameObject == null)
        {
            return "{\"ok\":false,\"error\":\"Object not found.\"}";
        }

        int depth = Mathf.Clamp(GetInt(query, "depth", 2), 0, 8);
        int maxChildren = Mathf.Clamp(GetInt(query, "maxChildren", 100), 1, 1000);
        return
            "{\"ok\":true,\"hierarchy\":" +
            TransformTree(gameObject.transform, depth, maxChildren) +
            "}";
    }

    private static string OpenInUnityExplorer(IDictionary<string, string> query)
    {
        GameObject? gameObject = ResolveGameObject(query);
        if (gameObject == null)
        {
            return "{\"ok\":false,\"error\":\"Object not found.\"}";
        }

        Type? managerType = AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(assembly => assembly.GetType("UnityExplorer.InspectorManager"))
            .FirstOrDefault(type => type != null);
        if (managerType == null)
        {
            return "{\"ok\":false,\"error\":\"UnityExplorer is not loaded.\"}";
        }

        System.Reflection.MethodInfo? inspectMethod = managerType
            .GetMethods(System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Static)
            .FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, "Inspect", StringComparison.Ordinal))
                {
                    return false;
                }

                System.Reflection.ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 2 && parameters[0].ParameterType == typeof(object);
            });
        if (inspectMethod == null)
        {
            return
                "{\"ok\":false,\"error\":\"UnityExplorer.InspectorManager.Inspect was not found.\"}";
        }

        inspectMethod.Invoke(null, new object?[] { gameObject, null });
        return
            "{\"ok\":true,\"opened\":" +
            ObjectSummary(gameObject) +
            "}";
    }

    private static string SetProperty(IDictionary<string, string> query)
    {
        GameObject? gameObject = ResolveGameObject(query);
        if (gameObject == null)
        {
            return "{\"ok\":false,\"error\":\"Object not found.\"}";
        }

        string componentName = Get(query, "component");
        string property = Get(query, "property");
        string value = Get(query, "value");
        Component? component = string.IsNullOrWhiteSpace(componentName)
            ? null
            : gameObject.GetComponents<Component>().FirstOrDefault(
                candidate =>
                    candidate != null &&
                    (string.Equals(
                         candidate.GetType().Name,
                         componentName,
                         StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(
                         candidate.GetType().FullName,
                         componentName,
                         StringComparison.OrdinalIgnoreCase)));

        if (string.Equals(componentName, "GameObject", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(property, "active", StringComparison.OrdinalIgnoreCase))
        {
            gameObject.SetActive(ParseBool(value));
        }
        else if (component is TMP_Text text)
        {
            if (string.Equals(property, "text", StringComparison.OrdinalIgnoreCase))
            {
                text.text = value;
            }
            else if (string.Equals(property, "fontSize", StringComparison.OrdinalIgnoreCase))
            {
                text.fontSize = ParseFloat(value);
            }
            else if (string.Equals(property, "enabled", StringComparison.OrdinalIgnoreCase))
            {
                text.enabled = ParseBool(value);
            }
            else
            {
                return UnsupportedProperty(componentName, property);
            }
        }
        else if (component is RectTransform rect)
        {
            Vector2 vector;
            if (string.Equals(property, "anchoredPosition", StringComparison.OrdinalIgnoreCase))
            {
                vector = ParseVector2(value);
                rect.anchoredPosition = vector;
            }
            else if (string.Equals(property, "sizeDelta", StringComparison.OrdinalIgnoreCase))
            {
                vector = ParseVector2(value);
                rect.sizeDelta = vector;
            }
            else if (string.Equals(property, "localScale", StringComparison.OrdinalIgnoreCase))
            {
                vector = ParseVector2(value);
                rect.localScale = new Vector3(vector.x, vector.y, rect.localScale.z);
            }
            else
            {
                return UnsupportedProperty(componentName, property);
            }
        }
        else if (component is Behaviour behaviour &&
                 string.Equals(property, "enabled", StringComparison.OrdinalIgnoreCase))
        {
            behaviour.enabled = ParseBool(value);
        }
        else
        {
            return UnsupportedProperty(componentName, property);
        }

        return
            "{\"ok\":true,\"object\":" +
            ObjectSummary(gameObject) +
            ",\"changed\":{" +
            $"\"component\":{Json(componentName)}," +
            $"\"property\":{Json(property)}," +
            $"\"value\":{Json(value)}" +
            "}}";
    }

    private static string UnsupportedProperty(string component, string property)
    {
        return
            "{\"ok\":false,\"error\":" +
            Json($"Unsupported property '{component}.{property}'.") +
            "}";
    }

    private static GameObject? ResolveGameObject(IDictionary<string, string> query)
    {
        int id = GetInt(query, "id", 0);
        string path = Get(query, "path");
        IEnumerable<GameObject> objects =
            Resources.FindObjectsOfTypeAll<GameObject>()
                .Where(gameObject => gameObject != null && gameObject.scene.IsValid());
        if (id != 0)
        {
            return objects.FirstOrDefault(gameObject => gameObject.GetInstanceID() == id);
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            return objects.FirstOrDefault(
                gameObject => string.Equals(
                    BuildPath(gameObject.transform),
                    path,
                    StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static string ObjectSummary(GameObject gameObject)
    {
        return
            "{" +
            $"\"id\":{gameObject.GetInstanceID()}," +
            $"\"name\":{Json(gameObject.name)}," +
            $"\"path\":{Json(BuildPath(gameObject.transform))}," +
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
            properties.Add($"\"parent\":{Json(rect.parent != null ? BuildPath(rect.parent) : string.Empty)}");
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
                $"\"handle\":{Json(slider.handleRect != null ? BuildPath(slider.handleRect) : string.Empty)}");
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

    private static string BuildPath(Transform transform)
    {
        var names = new Stack<string>();
        Transform? current = transform;
        while (current != null)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names);
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

    private sealed class PendingRequest
    {
        internal PendingRequest(string path, Dictionary<string, string> query)
        {
            Path = path;
            Query = query;
        }

        internal string Path { get; }

        internal Dictionary<string, string> Query { get; }

        internal AutoResetEvent Completed { get; } = new(false);

        internal string Response { get; set; } =
            "{\"ok\":false,\"error\":\"No response.\"}";

        internal int StatusCode { get; set; } = 500;
    }
}
