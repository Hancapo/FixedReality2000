using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FixedReality2000;

internal static class TextureFiltering
{
    private const int AnisotropicLevel = 16;
    private static readonly Dictionary<int, OriginalFilterState> OriginalFilters = new();
    private static AnisotropicFiltering? _originalAnisotropicFiltering;

    internal static void ApplyOriginalToLoadedTextures()
    {
        RestoreOriginalFiltering();

        if (!TryGetEnvironment(out Scene scene, out GameObject environment))
        {
            RestoreOriginalAnisotropicFiltering();
            return;
        }

        RememberOriginalAnisotropicFiltering();
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;

        HashSet<Texture> textures = CollectTextures(environment);
        int changed = 0;
        int alreadyConfigured = 0;
        int pointFiltered = 0;
        int withoutMipmaps = 0;
        int failed = 0;

        foreach (Texture texture in textures)
        {
            if (texture == null)
            {
                continue;
            }

            try
            {
                if (texture.filterMode == FilterMode.Point)
                {
                    pointFiltered++;
                    continue;
                }

                if (!HasMipmaps(texture))
                {
                    withoutMipmaps++;
                    continue;
                }

                RememberOriginalFilter(texture);
                if (texture.anisoLevel != AnisotropicLevel)
                {
                    texture.anisoLevel = AnisotropicLevel;
                    changed++;
                }
                else
                {
                    alreadyConfigured++;
                }
            }
            catch (Exception)
            {
                // Some engine-owned textures may reject changes. Continue with the rest.
                failed++;
            }
        }

        Plugin.Log.LogInfo(
            $"Anisotropic filtering applied to ENVIRONMENT in '{scene.name}': " +
            $"{changed} set to {AnisotropicLevel}x, {alreadyConfigured} already configured, " +
            $"{pointFiltered} point-filtered, {withoutMipmaps} without mipmaps, " +
            $"{textures.Count} inspected, {failed} skipped; global mode " +
            $"{QualitySettings.anisotropicFiltering}.");
    }

    internal static void ApplyNearestToLoadedTextures()
    {
        RestoreOriginalFiltering();
        RestoreOriginalAnisotropicFiltering();

        if (!TryGetEnvironment(out Scene scene, out GameObject environment))
        {
            return;
        }

        HashSet<Texture> textures = CollectColorTextures(environment);

        int filterModesChanged = 0;
        int withoutMipmaps = 0;
        int failed = 0;

        foreach (Texture texture in textures)
        {
            if (texture == null)
            {
                continue;
            }

            try
            {
                bool changeFilterMode = texture.filterMode != FilterMode.Point;
                if (changeFilterMode)
                {
                    RememberOriginalFilter(texture);
                    texture.filterMode = FilterMode.Point;
                    filterModesChanged++;
                }

                if (!HasMipmaps(texture))
                {
                    withoutMipmaps++;
                }
            }
            catch (Exception)
            {
                // Some engine-owned textures may reject changes. Continue with the rest.
                failed++;
            }
        }

        Plugin.Log.LogInfo(
            $"Nearest color filtering applied to ENVIRONMENT in '{scene.name}': " +
            $"{filterModesChanged} albedo/base textures set to Point, " +
            $"{withoutMipmaps} without mipmaps, " +
            $"{textures.Count} inspected, {failed} skipped. " +
            "Anisotropic filtering was left unchanged because it does not " +
            "apply to Point-filtered textures.");
    }

    internal static void RestoreOriginalFiltering()
    {
        int restored = 0;
        int failed = 0;

        foreach (OriginalFilterState state in OriginalFilters.Values)
        {
            if (!state.Texture.TryGetTarget(out Texture? texture) || texture == null)
            {
                continue;
            }

            try
            {
                texture.filterMode = state.FilterMode;
                texture.anisoLevel = state.AnisoLevel;
                restored++;
            }
            catch (Exception)
            {
                failed++;
            }
        }

        OriginalFilters.Clear();

        if (restored > 0 || failed > 0)
        {
            Plugin.Log.LogInfo($"Texture filtering restored: {restored} restored, {failed} skipped.");
        }
    }

    private static void RememberOriginalFilter(Texture texture)
    {
        int instanceId = texture.GetInstanceID();
        if (OriginalFilters.TryGetValue(instanceId, out OriginalFilterState? existing) &&
            existing.Texture.TryGetTarget(out Texture? trackedTexture) &&
            ReferenceEquals(trackedTexture, texture))
        {
            return;
        }

        OriginalFilters[instanceId] = new OriginalFilterState(
            new WeakReference<Texture>(texture),
            texture.filterMode,
            texture.anisoLevel);
    }

    internal static void RestoreOriginalAnisotropicFiltering()
    {
        if (!_originalAnisotropicFiltering.HasValue)
        {
            return;
        }

        QualitySettings.anisotropicFiltering = _originalAnisotropicFiltering.Value;
        _originalAnisotropicFiltering = null;
    }

    private static void RememberOriginalAnisotropicFiltering()
    {
        _originalAnisotropicFiltering ??= QualitySettings.anisotropicFiltering;
    }

    private static bool TryGetEnvironment(
        out Scene scene,
        out GameObject environment)
    {
        scene = SceneManager.GetActiveScene();
        environment = null!;
        if (!scene.IsValid() ||
            string.Equals(scene.name, "00_room", StringComparison.OrdinalIgnoreCase))
        {
            Plugin.Log.LogInfo(
                $"Texture filtering skipped in scene '{scene.name}': " +
                "Natem world filtering is disabled here.");
            return false;
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (string.Equals(root.name, "ENVIRONMENT", StringComparison.Ordinal))
            {
                environment = root;
                return true;
            }
        }

        Plugin.Log.LogInfo(
            $"Texture filtering skipped in scene '{scene.name}': " +
            "no ENVIRONMENT root was found.");
        return false;
    }

    private static HashSet<Texture> CollectTextures(GameObject environment)
    {
        var textures = new HashSet<Texture>();
        Renderer[] renderers =
            environment.GetComponentsInChildren<Renderer>(includeInactive: true);
        foreach (Renderer renderer in renderers)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null)
                {
                    continue;
                }

                try
                {
                    foreach (string propertyName in material.GetTexturePropertyNames())
                    {
                        Texture texture = material.GetTexture(propertyName);
                        if (texture != null && texture is not RenderTexture)
                        {
                            textures.Add(texture);
                        }
                    }
                }
                catch (Exception)
                {
                    // A malformed or engine-owned material should not abort the scene pass.
                }
            }
        }

        return textures;
    }

    private static HashSet<Texture> CollectColorTextures(GameObject environment)
    {
        var textures = new HashSet<Texture>();
        Renderer[] renderers =
            environment.GetComponentsInChildren<Renderer>(includeInactive: true);
        foreach (Renderer renderer in renderers)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null)
                {
                    continue;
                }

                try
                {
                    foreach (string propertyName in material.GetTexturePropertyNames())
                    {
                        if (!IsColorTextureProperty(propertyName))
                        {
                            continue;
                        }

                        Texture texture = material.GetTexture(propertyName);
                        if (texture != null && texture is not RenderTexture)
                        {
                            textures.Add(texture);
                        }
                    }
                }
                catch (Exception)
                {
                    // A malformed or engine-owned material should not abort the scene pass.
                }
            }
        }

        return textures;
    }

    private static bool IsColorTextureProperty(string propertyName)
    {
        return string.Equals(
                   propertyName,
                   "_MainTex",
                   StringComparison.OrdinalIgnoreCase) ||
               propertyName.IndexOf(
                   "BaseMap",
                   StringComparison.OrdinalIgnoreCase) >= 0 ||
               propertyName.IndexOf(
                   "Albedo",
                   StringComparison.OrdinalIgnoreCase) >= 0 ||
               propertyName.IndexOf(
                   "Diffuse",
                   StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool HasMipmaps(Texture texture)
    {
        return texture switch
        {
            Texture2D texture2D => texture2D.mipmapCount > 1,
            Cubemap cubemap => cubemap.mipmapCount > 1,
            Texture2DArray array => array.mipmapCount > 1,
            CubemapArray array => array.mipmapCount > 1,
            _ => true
        };
    }

    private sealed class OriginalFilterState
    {
        internal OriginalFilterState(
            WeakReference<Texture> texture,
            FilterMode filterMode,
            int anisoLevel)
        {
            Texture = texture;
            FilterMode = filterMode;
            AnisoLevel = anisoLevel;
        }

        internal WeakReference<Texture> Texture { get; }
        internal FilterMode FilterMode { get; }
        internal int AnisoLevel { get; }
    }
}
