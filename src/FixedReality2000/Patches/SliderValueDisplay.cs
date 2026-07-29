using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FixedReality2000.Patches;

internal sealed class SliderValueDisplay : MonoBehaviour
{
    private enum ValueFormat
    {
        Percent,
        Degrees,
        Integer,
        Decimal
    }

    private Slider? _slider;
    private RectTransform? _handle;
    private RectTransform? _labelRect;
    private TMP_Text? _label;
    private ValueFormat _format;

    internal static void AttachToOptionsPanel(
        Transform optionsPanel,
        TMP_Text template)
    {
        Slider[] sliders =
            optionsPanel.GetComponentsInChildren<Slider>(includeInactive: true);
        foreach (Slider slider in sliders)
        {
            if (slider.handleRect == null ||
                slider.GetComponent<SliderValueDisplay>() != null)
            {
                continue;
            }

            SliderValueDisplay display =
                slider.gameObject.AddComponent<SliderValueDisplay>();
            display.Initialize(slider, template);
        }
    }

    private void Initialize(Slider slider, TMP_Text template)
    {
        _slider = slider;
        _handle = slider.handleRect;
        _format = InferFormat(slider);

        _label = Instantiate(template, slider.transform, worldPositionStays: false);
        _label.gameObject.name = "FixedReality2000_Value";
        _label.gameObject.SetActive(true);
        _label.raycastTarget = false;
        _label.enableAutoSizing = false;
        _label.fontSharedMaterial = template.fontSharedMaterial;
        _label.fontSize = 18f;
        _label.alignment = TextAlignmentOptions.Center;
        _label.text = string.Empty;

        _labelRect = (RectTransform)_label.transform;
        _labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        _labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        _labelRect.pivot = new Vector2(0.5f, 1f);
        _labelRect.sizeDelta = new Vector2(100f, 28f);
        _labelRect.localScale = Vector3.one;
        _labelRect.localRotation = Quaternion.identity;
        _labelRect.SetAsLastSibling();

        slider.onValueChanged.AddListener(UpdateValue);
        UpdateDisplay();
    }

    private void OnEnable()
    {
        UpdateDisplay();
    }

    private void LateUpdate()
    {
        UpdateDisplay();
    }

    private void OnDestroy()
    {
        if (_slider != null)
        {
            _slider.onValueChanged.RemoveListener(UpdateValue);
        }
    }

    private void UpdateValue(float _)
    {
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (_slider == null ||
            _handle == null ||
            _label == null ||
            _labelRect == null)
        {
            return;
        }

        _label.text = FormatValue(_slider);

        Vector3 handleBottom =
            _handle.TransformPoint(
                new Vector3(
                    _handle.rect.center.x,
                    _handle.rect.yMin,
                    0f));
        Vector3 localPosition =
            ((RectTransform)_slider.transform).InverseTransformPoint(handleBottom);
        _labelRect.anchoredPosition =
            new Vector2(localPosition.x, localPosition.y - 5f);
    }

    private string FormatValue(Slider slider)
    {
        return _format switch
        {
            ValueFormat.Percent =>
                $"{Mathf.RoundToInt(Mathf.InverseLerp(slider.minValue, slider.maxValue, slider.value) * 100f)}%",
            ValueFormat.Degrees => $"{Mathf.RoundToInt(slider.value)}°",
            ValueFormat.Integer => Mathf.RoundToInt(slider.value).ToString(),
            _ => slider.value.ToString("0.00")
        };
    }

    private static ValueFormat InferFormat(Slider slider)
    {
        if (slider.gameObject.name.StartsWith("FixedReality2000_Fov"))
        {
            return ValueFormat.Degrees;
        }

        if (slider.GetComponent<VolumeSliderManager>() != null)
        {
            return ValueFormat.Percent;
        }

        return slider.maxValue > 10f
            ? ValueFormat.Integer
            : ValueFormat.Decimal;
    }
}
