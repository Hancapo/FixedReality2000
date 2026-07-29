using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Rendering;

namespace FixedReality2000;

internal static class RenderBatchingOptimizations
{
    private static readonly Dictionary<int, PipelineState> OriginalPipelines = new();
    private static bool? _originalGlobalSrpBatcher;

    internal static void Apply()
    {
        PruneDestroyedPipelines();

        _originalGlobalSrpBatcher ??=
            GraphicsSettings.useScriptableRenderPipelineBatching;
        GraphicsSettings.useScriptableRenderPipelineBatching = true;

        RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
        if (pipeline == null)
        {
            return;
        }

        RememberPipeline(pipeline);
        WriteBooleanProperty(pipeline, "useSRPBatcher", true);
        WriteBooleanProperty(pipeline, "supportsDynamicBatching", true);

        Plugin.Log.LogInfo(
            "Enabled the SRP Batcher and URP dynamic batching.");
    }

    internal static void RestoreOriginalSettings()
    {
        foreach (PipelineState state in OriginalPipelines.Values)
        {
            if (!state.Pipeline.TryGetTarget(out RenderPipelineAsset? pipeline) ||
                pipeline == null)
            {
                continue;
            }

            RestoreProperty(pipeline, "useSRPBatcher", state.UseSrpBatcher);
            RestoreProperty(
                pipeline,
                "supportsDynamicBatching",
                state.SupportsDynamicBatching);
        }

        OriginalPipelines.Clear();

        if (_originalGlobalSrpBatcher.HasValue)
        {
            GraphicsSettings.useScriptableRenderPipelineBatching =
                _originalGlobalSrpBatcher.Value;
            _originalGlobalSrpBatcher = null;
        }
    }

    private static void RememberPipeline(RenderPipelineAsset pipeline)
    {
        int instanceId = pipeline.GetInstanceID();
        if (OriginalPipelines.TryGetValue(instanceId, out PipelineState? state) &&
            state.Pipeline.TryGetTarget(out RenderPipelineAsset? tracked) &&
            ReferenceEquals(tracked, pipeline))
        {
            return;
        }

        OriginalPipelines[instanceId] = new PipelineState(
            new WeakReference<RenderPipelineAsset>(pipeline),
            ReadBooleanProperty(pipeline, "useSRPBatcher", true),
            ReadBooleanProperty(pipeline, "supportsDynamicBatching", false));
    }

    private static bool ReadBooleanProperty(
        RenderPipelineAsset pipeline,
        string propertyName,
        bool fallback)
    {
        PropertyInfo? property = pipeline.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public);
        return property?.GetValue(pipeline) is bool value ? value : fallback;
    }

    private static void WriteBooleanProperty(
        RenderPipelineAsset pipeline,
        string propertyName,
        bool value)
    {
        PropertyInfo? property = pipeline.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public);
        if (property?.CanWrite == true)
        {
            property.SetValue(pipeline, value);
        }
    }

    private static void RestoreProperty(
        RenderPipelineAsset pipeline,
        string propertyName,
        bool value)
    {
        try
        {
            WriteBooleanProperty(pipeline, propertyName, value);
        }
        catch (Exception)
        {
            // The pipeline can be disposed while Unity changes quality levels.
        }
    }

    private static void PruneDestroyedPipelines()
    {
        var destroyed = new List<int>();
        foreach (KeyValuePair<int, PipelineState> pair in OriginalPipelines)
        {
            if (!pair.Value.Pipeline.TryGetTarget(out RenderPipelineAsset? pipeline) ||
                pipeline == null)
            {
                destroyed.Add(pair.Key);
            }
        }

        foreach (int instanceId in destroyed)
        {
            OriginalPipelines.Remove(instanceId);
        }
    }

    private sealed class PipelineState
    {
        internal PipelineState(
            WeakReference<RenderPipelineAsset> pipeline,
            bool useSrpBatcher,
            bool supportsDynamicBatching)
        {
            Pipeline = pipeline;
            UseSrpBatcher = useSrpBatcher;
            SupportsDynamicBatching = supportsDynamicBatching;
        }

        internal WeakReference<RenderPipelineAsset> Pipeline { get; }
        internal bool UseSrpBatcher { get; }
        internal bool SupportsDynamicBatching { get; }
    }
}
