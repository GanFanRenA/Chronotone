using System.Collections.Generic;
using UnityEngine;

public class BasicJudgementFeedback : MonoBehaviour
{
    private sealed class AnimatedSprite
    {
        public Transform Transform;
        public SpriteRenderer Renderer;
        public Vector3 StartPosition;
        public Vector3 EndPosition;
        public Vector3 BaseScale;
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

    [Header("世界尺寸")]
    [Tooltip("将上述尺寸参数换算为世界单位；0.01 约等于 1080p 下每参考像素的世界距离")]
    [SerializeField, Min(0.0001f)] private float worldUnitsPerReferencePixel = 0.01f;

    [Header("Diamond (菱形闪光)")]
    [SerializeField, Min(0f)] private float perfectDiamondSize = 42f;
    [SerializeField, Min(0f)] private float greatDiamondSize = 36f;
    [SerializeField, Min(0f)] private float missDiamondSize = 30f;

    [Header("Perfect 碎片")]
    [SerializeField, Min(0f)] private float shardSize = 13f;
    [SerializeField, Min(0f)] private float shardDistance = 74f;

    [Header("Bad / Miss 叉号")]
    [SerializeField] private Vector2 crossSize = new Vector2(64f, 10f);

    private readonly List<AnimatedSprite> activeEffects = new List<AnimatedSprite>();

    private Sprite effectSprite;
    private Material effectMaterial;
    private int effectSortingLayerId;
    private int effectSortingOrder = 100;

    /// <summary>运行时也可以直接修改这个值。</summary>
    public float SizeMultiplier
    {
        get => sizeMultiplier;
        set => sizeMultiplier = Mathf.Max(0.01f, value);
    }

    void Awake()
    {
        CacheEffectTemplate();
    }

    void Update()
    {
        float deltaTime = Time.unscaledDeltaTime;

        for (int i = activeEffects.Count - 1; i >= 0; i--)
        {
            AnimatedSprite effect = activeEffects[i];
            effect.Age += deltaTime;

            float progress = Mathf.Clamp01(effect.Age / effect.Duration);
            float eased = progress * progress * (3f - 2f * progress);

            effect.Transform.position =
                Vector3.Lerp(effect.StartPosition, effect.EndPosition, eased);
            effect.Transform.localScale =
                Vector3.Scale(
                    effect.BaseScale,
                    Vector3.Lerp(effect.StartScale, effect.EndScale, eased));
            effect.Transform.localEulerAngles =
                new Vector3(0f, 0f, Mathf.Lerp(effect.StartRotation, effect.EndRotation, eased));

            Color color = effect.Color;
            color.a = effect.Color.a * (1f - progress);
            effect.Renderer.color = color;

            if (progress >= 1f)
            {
                Destroy(effect.Renderer.gameObject);
                activeEffects.RemoveAt(i);
            }
        }
    }

    public void ShowJudgement(JudgementGrade grade, Vector3 worldPosition)
    {
        Color color = GetColor(grade);

        switch (grade)
        {
            case JudgementGrade.Perfect:
                SpawnDiamond(worldPosition, color, perfectDiamondSize, 2.4f, 0.32f);
                SpawnPerfectShards(worldPosition, color);
                break;

            case JudgementGrade.Great:
                SpawnDiamond(worldPosition, color, greatDiamondSize, 2.7f, 0.3f);
                break;

            case JudgementGrade.Bad:
                SpawnCross(worldPosition, color, 0.24f);
                break;

            case JudgementGrade.Miss:
                SpawnDiamond(worldPosition, color, missDiamondSize, 2.2f, 0.28f);
                SpawnCross(worldPosition, color, 0.24f);
                break;
        }
    }

    private void CacheEffectTemplate()
    {
        SpriteRenderer sourceRenderer =
            GetComponentInParent<SpriteRenderer>();
        if (sourceRenderer != null)
        {
            effectSprite = sourceRenderer.sprite;
            effectMaterial = sourceRenderer.sharedMaterial;
            effectSortingLayerId = sourceRenderer.sortingLayerID;
            effectSortingOrder = sourceRenderer.sortingOrder + 100;
        }

        if (effectSprite == null)
        {
            effectSprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f);
        }
    }

    private void SpawnDiamond(
        Vector3 position,
        Color color,
        float size,
        float endScale,
        float duration)
    {
        CreateSpriteEffect(
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

    private void SpawnPerfectShards(Vector3 position, Color color)
    {
        Vector3[] directions =
        {
            Vector3.right,
            Vector3.left,
            Vector3.up,
            Vector3.down
        };

        float distance = ToWorldLength(shardDistance);

        for (int i = 0; i < directions.Length; i++)
        {
            CreateSpriteEffect(
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

    private void SpawnCross(Vector3 position, Color color, float duration)
    {
        for (int i = 0; i < 2; i++)
        {
            float rotation = i == 0 ? 45f : -45f;
            CreateSpriteEffect(
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

    private void CreateSpriteEffect(
        string effectName,
        Vector3 startPosition,
        Vector2 size,
        Vector3 endPosition,
        Vector3 startScale,
        Vector3 endScale,
        float startRotation,
        float endRotation,
        Color color,
        float duration)
    {
        if (effectSprite == null)
            return;

        var effectObject = new GameObject(effectName);
        var spriteRenderer = effectObject.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = effectSprite;
        if (effectMaterial != null)
            spriteRenderer.sharedMaterial = effectMaterial;
        spriteRenderer.color = color;
        spriteRenderer.sortingLayerID = effectSortingLayerId;
        spriteRenderer.sortingOrder = effectSortingOrder;

        Transform effectTransform = effectObject.transform;
        effectTransform.position = startPosition;
        effectTransform.localRotation =
            Quaternion.Euler(0f, 0f, startRotation);

        Vector3 baseScale = GetBaseScaleForSize(size);
        effectTransform.localScale =
            Vector3.Scale(baseScale, startScale);

        activeEffects.Add(new AnimatedSprite
        {
            Transform = effectTransform,
            Renderer = spriteRenderer,
            StartPosition = startPosition,
            EndPosition = endPosition,
            BaseScale = baseScale,
            StartScale = startScale,
            EndScale = endScale,
            StartRotation = startRotation,
            EndRotation = endRotation,
            Color = color,
            Duration = duration
        });
    }

    private Vector3 GetBaseScaleForSize(Vector2 size)
    {
        Vector2 worldSize = new Vector2(
            size.x * sizeMultiplier * worldUnitsPerReferencePixel,
            size.y * sizeMultiplier * worldUnitsPerReferencePixel);
        Vector3 spriteSize = effectSprite.bounds.size;

        return new Vector3(
            spriteSize.x > 0.0001f ? worldSize.x / spriteSize.x : worldSize.x,
            spriteSize.y > 0.0001f ? worldSize.y / spriteSize.y : worldSize.y,
            1f);
    }

    private float ToWorldLength(float referencePixels)
    {
        return referencePixels * sizeMultiplier * worldUnitsPerReferencePixel;
    }

    private static Color GetColor(JudgementGrade grade)
    {
        return grade == JudgementGrade.Perfect ? PerfectColor : grade == JudgementGrade.Great ? GreatColor : BadColor;
    }
}
