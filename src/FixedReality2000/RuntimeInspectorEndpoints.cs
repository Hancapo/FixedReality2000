using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;

namespace FixedReality2000;

internal sealed partial class RuntimeInspectorBridge
{
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
                    TransformPathUtility.GetPath(gameObject.transform).IndexOf(
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
            .OrderBy(gameObject =>
                TransformPathUtility.GetPath(gameObject.transform))
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
                    TransformPathUtility.GetPath(gameObject.transform),
                    path,
                    StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

}
