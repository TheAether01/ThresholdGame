using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Scales a UI Image's RectTransform to cover its parent while preserving
/// the sprite's native aspect ratio (CSS "background-size: cover" behavior).
/// Attach this to a UI Image that is anchored to stretch-fill its parent.
/// </summary>
[RequireComponent(typeof(Image))]
[ExecuteAlways]
public class CoverBackground : MonoBehaviour
{
    private Image _image;
    private RectTransform _rect;
    private RectTransform _parentRect;

    void Awake()
    {
        _image = GetComponent<Image>();
        _rect = GetComponent<RectTransform>();
    }

    void Start()
    {
        ApplyCover();
    }

    void OnRectTransformDimensionsChange()
    {
        ApplyCover();
    }

    private void ApplyCover()
    {
        if (_image == null || _image.sprite == null) return;
        if (_rect == null) return;

        // Get parent rect (the Canvas or container)
        if (_parentRect == null)
        {
            _parentRect = _rect.parent as RectTransform;
            if (_parentRect == null) return;
        }

        float parentW = _parentRect.rect.width;
        float parentH = _parentRect.rect.height;

        if (parentW <= 0 || parentH <= 0) return;

        // Native sprite dimensions
        Rect spriteRect = _image.sprite.rect;
        float spriteW = spriteRect.width;
        float spriteH = spriteRect.height;

        if (spriteW <= 0 || spriteH <= 0) return;

        float parentAspect = parentW / parentH;
        float spriteAspect = spriteW / spriteH;

        float finalW, finalH;

        if (spriteAspect > parentAspect)
        {
            // Sprite is wider than parent — match height, overflow width
            finalH = parentH;
            finalW = parentH * spriteAspect;
        }
        else
        {
            // Sprite is taller than parent — match width, overflow height
            finalW = parentW;
            finalH = parentW / spriteAspect;
        }

        // Center the anchors and set exact size
        _rect.anchorMin = new Vector2(0.5f, 0.5f);
        _rect.anchorMax = new Vector2(0.5f, 0.5f);
        _rect.anchoredPosition = Vector2.zero;
        _rect.sizeDelta = new Vector2(finalW, finalH);
    }
}
