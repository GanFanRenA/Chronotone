using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class BasicJudgementFeedback : MonoBehaviour
{
    private sealed class AnimatedGraphic
    {
        public RectTransform Rect;
        public UnityEngine.UI.Graphic Graphic;
        public Vector2 StartPosition;
        public Vector2 EndPosition;
        public Vector3 StartScale;
        public Vector3 EndScale;
        public float StartRotation;
        public float EndRotation;
        public Color Color;
        public float Duration;
        public float Age;
    }

    private static readonly Color PerfectColor = new Color(1f, 0.72f, 0.12f, 1f);
    private static readonly Color GreatColor = new Color(0.22f, 0.62f, 1f, 1f);
    private static readonly Color BadColor = new Color(1f, 0.18f, 0.16f, 1f);

    [Header("整体缩放")]
    [Tooltip("所有特效的尺寸与位移都会乘以该系数")]
    [SerializeField, Range(0.1f, 5f)] private float sizeMultiplier = 1f;

    [Header("Diamond (菱形闪光)")]
    [SerializeField, Min(0f)] private float perfectDiamondSize = 42f;
    [SerializeField, Min(0f)] private float greatDiamondSize = 36f;
    [SerializeField, Min(0f)] private float missDiamondSize = 30f;

    [Header("Perfect 碎片")]
    [SerializeField, Min(0f)] private float shardSize = 13f;
    [SerializeField, Min(0f)] private float shardDistance = 74f;

    [Header("Bad / Miss 叉号")]
    [SerializeField] private Vector2 crossSize = new Vector2(64f, 10f);

    private readonly List<AnimatedGraphic> activeEffects = new List<AnimatedGraphic>();

    private Canvas canvas;
    private RectTransform canvasRect;

    /// <summary>运行时也可以直接修改这个值。</summary>
    public float SizeMultiplier
    {
        get => sizeMultiplier;
        set => sizeMultiplier = Mathf.Max(0.01f, value);
    }

    void Awake()
    {
        BuildCanvas();
    }

    void Update()
    {
        float deltaTime = Time.unscaledDeltaTime;

        for (int i = activeEffects.Count - 1; i >= 0; i--)
        {
            AnimatedGraphic effect = activeEffects[i];
            effect.Age += deltaTime;

            float progress = Mathf.Clamp01(effect.Age / effect.Duration);
            float eased = progress * progress * (3f - 2f * progress);

            effect.Rect.anchoredPosition = Vector2.Lerp(effect.StartPosition, effect.EndPosition, eased);
            effect.Rect.localScale = Vector3.Lerp(effect.StartScale, effect.EndScale, eased);
            effect.Rect.localEulerAngles =
                new Vector3(0f, 0f, Mathf.Lerp(effect.StartRotation, effect.EndRotation, eased));

            Color color = effect.Color;
            color.a = effect.Color.a * (1f - progress);
            effect.Graphic.color = color;

            if (progress >= 1f)
            {
                Destroy(effect.Graphic.gameObject);
                activeEffects.RemoveAt(i);
            }
        }
    }

    public void ShowJudgement(JudgementGrade grade, Vector2 screenPosition)
    {
        if (!TryGetCanvasPosition(screenPosition, out Vector2 localPosition))
            return;

        Color color = GetColor(grade);

        switch (grade)
        {
            case JudgementGrade.Perfect:
                SpawnDiamond(localPosition, color, perfectDiamondSize, 2.4f, 0.32f);
                SpawnPerfectShards(localPosition, color);
                break;

            case JudgementGrade.Great:
                SpawnDiamond(localPosition, color, greatDiamondSize, 2.7f, 0.3f);
                break;

            case JudgementGrade.Bad:
                SpawnCross(localPosition, color, 0.24f);
                break;

            case JudgementGrade.Miss:
                SpawnDiamond(localPosition, color, missDiamondSize, 2.2f, 0.28f);
                SpawnCross(localPosition, color, 0.24f);
                break;
        }
    }

    private void BuildCanvas()
    {
        var canvasObject = new GameObject(
            "JudgementCanvas",
            typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler),
            typeof(UnityEngine.UI.GraphicRaycaster));

        canvasObject.transform.SetParent(transform, false);

        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasRect = canvasObject.GetComponent<RectTransform>();
    }

    private void SpawnDiamond(Vector2 position, Color color, float size, float endScale, float duration)
    {
        CreateImageEffect(
            "DiamondBurst",
            position,
            new Vector2(size, size),
            position,
            Vector3.one,
            Vector3.one * endScale,
            45f,
            135f,
            color,
            duration);
    }

    private void SpawnPerfectShards(Vector2 position, Color color)
    {
        Vector2[] directions =
        {
            new Vector2(1f, 0f),
            new Vector2(-1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(0f, -1f)
        };

        float distance = shardDistance * sizeMultiplier;

        for (int i = 0; i < directions.Length; i++)
        {
            CreateImageEffect(
                "PerfectShard",
                position,
                new Vector2(shardSize, shardSize),
                position + directions[i] * distance,
                Vector3.one,
                Vector3.one * 0.35f,
                45f,
                135f,
                color,
                0.34f);
        }
    }

    private void SpawnCross(Vector2 position, Color color, float duration)
    {
        for (int i = 0; i < 2; i++)
        {
            float rotation = i == 0 ? 45f : -45f;
            CreateImageEffect(
                "RedCross",
                position,
                crossSize,
                position,
                Vector3.one,
                new Vector3(1.55f, 0.85f, 1f),
                rotation,
                rotation + (i == 0 ? 22f : -22f),
                color,
                duration);
        }
    }

    private void CreateImageEffect(
        string effectName,
        Vector2 startPosition,
        Vector2 size,
        Vector2 endPosition,
        Vector3 startScale,
        Vector3 endScale,
        float startRotation,
        float endRotation,
        Color color,
        float duration)
    {
        var effectObject = new GameObject(effectName, typeof(RectTransform));
        effectObject.transform.SetParent(canvas.transform, false);

        var rect = (RectTransform)effectObject.transform;
        ConfigureCenteredRect(rect, size * sizeMultiplier);
        rect.anchoredPosition = startPosition;
        rect.localRotation = Quaternion.Euler(0f, 0f, startRotation);
        rect.localScale = startScale;

        var image = effectObject.AddComponent<UnityEngine.UI.Image>();
        image.raycastTarget = false;
        image.color = color;

        activeEffects.Add(new AnimatedGraphic
        {
            Rect = rect,
            Graphic = image,
            StartPosition = startPosition,
            EndPosition = endPosition,
            StartScale = startScale,
            EndScale = endScale,
            StartRotation = startRotation,
            EndRotation = endRotation,
            Color = color,
            Duration = duration
        });
    }

    private static Color GetColor(JudgementGrade grade)
    {
        return grade == JudgementGrade.Perfect ? PerfectColor : grade == JudgementGrade.Great ? GreatColor : BadColor;
    }

    private bool TryGetCanvasPosition(Vector2 screenPosition, out Vector2 localPosition)
    {
        localPosition = Vector2.zero;
        if (canvasRect == null)
            return false;

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            screenPosition,
            null,
            out localPosition);
    }

    private static void ConfigureCenteredRect(RectTransform rect, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
    }
}