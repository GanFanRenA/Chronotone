using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class BasicJudgementFeedback : MonoBehaviour
{
    private sealed class AnimatedSprite
    {
        public GameObject GameObject;
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
        public bool InUse;
    }

    private static readonly Color PerfectColor = new Color(1f, 0.72f, 0.12f, 1f);
    private static readonly Color GreatColor = new Color(0.22f, 0.62f, 1f, 1f);
    private static readonly Color BadColor = new Color(1f, 0.18f, 0.16f, 1f);

    [Header("Successful Timing Feedback")]
    [FormerlySerializedAs("earlyFlashTintStrength")]
    [SerializeField, Range(0f, 1f)] private float timingTintStrength = 0.85f;
    [SerializeField, Min(0f)] private float ghostLifetime = 1f;
    [SerializeField, Range(0f, 1f)] private float ghostStartAlpha = 0.65f;
    [SerializeField, Min(0f)] private float ghostEndScale = 0.96f;
    [SerializeField, Min(0f)] private float timingEchoDistancePerSecond = 1.1f;
    [SerializeField, Min(0f)] private float timingEchoMinDistance = 0.035f;
    [SerializeField, Min(0f)] private float timingEchoMaxDistance = 0.12f;
    [SerializeField] private Vector3 timingEchoForward = Vector3.right;
    [SerializeField, Min(1)] private int effectPoolSize = 20;

    [System.NonSerialized] private float sizeMultiplier;
    [System.NonSerialized] private float worldUnitsPerReferencePixel;
    [System.NonSerialized] private float perfectDiamondSize;
    [System.NonSerialized] private float greatDiamondSize;
    [System.NonSerialized] private float missDiamondSize;
    [System.NonSerialized] private float shardSize;
    [System.NonSerialized] private float shardDistance;
    [System.NonSerialized] private Vector2 crossSize;
    [System.NonSerialized] private bool settingsApplied;

    private readonly List<AnimatedSprite> activeEffects = new List<AnimatedSprite>();

    private Sprite effectSprite;
    private Material effectMaterial;
    private int effectSortingLayerId;
    private int effectSortingOrder = 100;
    private SpriteRenderer playerRenderer;
    private SpriteRenderer timingGhostRenderer;
    private float ghostElapsed;
    private float ghostDuration;
    private bool missingGhostRendererReported;
    private Color ghostColor;
    private Vector3 ghostPosition;
    private Quaternion ghostRotation;
    private Vector3 ghostScale;
    private Vector3 feedbackLocalPosition;
    private Quaternion feedbackLocalRotation;
    private Vector3 feedbackLocalScale;

    /// <summary>运行时也可以直接修改这个值。</summary>
    public float SizeMultiplier
    {
        get
        {
            EnsureSettings();
            return sizeMultiplier;
        }
        set
        {
            sizeMultiplier = Mathf.Clamp(value, 0.1f, 5f);
            GameSettingsSO.LoadDefault().SizeMultiplier =
                sizeMultiplier;
        }
    }

    void Awake()
    {
        EnsureSettings();
        CacheEffectTemplate();
        CacheTimingFeedbackTargets();
        BuildEffectPool();
    }

    private void EnsureSettings()
    {
        if (settingsApplied)
            return;

        Player owner = GetComponentInParent<Player>();
        GameSettingsSO settings = owner != null
            ? owner.GameSettings
            : GameSettingsSO.LoadDefault();
        sizeMultiplier = settings.SizeMultiplier;
        worldUnitsPerReferencePixel =
            settings.WorldUnitsPerReferencePixel;
        perfectDiamondSize = settings.PerfectDiamondSize;
        greatDiamondSize = settings.GreatDiamondSize;
        missDiamondSize = settings.MissDiamondSize;
        shardSize = settings.ShardSize;
        shardDistance = settings.ShardDistance;
        crossSize = settings.CrossSize;
        settingsApplied = true;
    }

    void Update()
    {
        float deltaTime = Time.unscaledDeltaTime;
        UpdateTimingGhost(deltaTime);

        for (int i = activeEffects.Count - 1; i >= 0; i--)
        {
            AnimatedSprite effect = activeEffects[i];
            if (!effect.InUse)
                continue;

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
                ReleaseEffect(effect);
            }
        }
    }

    void OnDisable()
    {
        StopTimingGhost();
        foreach (AnimatedSprite effect in activeEffects)
            ReleaseEffect(effect);
    }

    /// <summary>
    /// 使用判定结果已有的 TimeDelta 驱动成功反馈。
    /// 正拍交给 PlayerVisualFeedback；只有早晚偏差留下有方向的短回声。
    /// </summary>
    public void ShowTimingSuccess(JudgementResult result)
    {
        if (!result.IsSuccessfulHit)
            return;

        float absoluteDelta = Mathf.Abs((float)result.TimeDelta);
        if (absoluteDelta <= 0.006f)
        {
            StopTimingGhost();
            return;
        }

        ShowTimingGhost(GetColor(result.Grade), result.TimeDelta);
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
        Transform parent = transform.parent;
        Transform visualRoot = parent != null
            ? parent.Find("VisualRoot")
            : null;
        playerRenderer = visualRoot != null
            ? visualRoot.GetComponent<SpriteRenderer>()
            : null;
        if (playerRenderer == null && parent != null)
            playerRenderer = parent.GetComponentInParent<SpriteRenderer>();

        if (playerRenderer != null)
        {
            effectSprite = playerRenderer.sprite;
            effectMaterial = playerRenderer.sharedMaterial;
            effectSortingLayerId = playerRenderer.sortingLayerID;
            effectSortingOrder = playerRenderer.sortingOrder + 100;
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

    private void CacheTimingFeedbackTargets()
    {
        TryGetComponent(out timingGhostRenderer);
        if (timingGhostRenderer != null)
            timingGhostRenderer.enabled = false;

        feedbackLocalPosition = transform.localPosition;
        feedbackLocalRotation = transform.localRotation;
        feedbackLocalScale = transform.localScale;
    }

    private void ShowTimingGhost(Color gradeColor, double timeDelta)
    {
        if (timingGhostRenderer == null)
        {
            if (!missingGhostRendererReported)
            {
                Debug.LogWarning(
                    "早晚方向回声需要 JudgementFeedback 上的 SpriteRenderer。",
                    this);
                missingGhostRendererReported = true;
            }
            return;
        }

        if (playerRenderer == null)
            return;

        Transform source = playerRenderer.transform;
        float absoluteDelta = Mathf.Abs((float)timeDelta);
        float echoDistance = Mathf.Clamp(
            absoluteDelta * timingEchoDistancePerSecond,
            timingEchoMinDistance,
            timingEchoMaxDistance);
        Vector3 echoDirection = timeDelta < 0d
            ? timingEchoForward.normalized
            : -timingEchoForward.normalized;
        ghostPosition = source.position + echoDirection * echoDistance;
        ghostRotation = source.rotation;
        ghostScale = source.localScale;
        ghostColor = Color.Lerp(
            playerRenderer.color,
            gradeColor,
            timingTintStrength);
        ghostColor.a = ghostStartAlpha;
        ghostElapsed = 0f;
        ghostDuration = Mathf.Clamp(
            0.08f + absoluteDelta * 0.45f,
            0.08f,
            Mathf.Max(0.08f, ghostLifetime));

        timingGhostRenderer.sprite = playerRenderer.sprite;
        timingGhostRenderer.sharedMaterial = playerRenderer.sharedMaterial;
        timingGhostRenderer.flipX = playerRenderer.flipX;
        timingGhostRenderer.flipY = playerRenderer.flipY;
        timingGhostRenderer.sortingLayerID =
            playerRenderer.sortingLayerID;
        timingGhostRenderer.sortingOrder =
            playerRenderer.sortingOrder;
        timingGhostRenderer.color = ghostColor;
        timingGhostRenderer.transform.SetPositionAndRotation(
            ghostPosition,
            ghostRotation);
        timingGhostRenderer.transform.localScale = ghostScale;
        timingGhostRenderer.enabled = true;
    }

    private void UpdateTimingGhost(float deltaTime)
    {
        if (timingGhostRenderer == null ||
            !timingGhostRenderer.enabled)
            return;

        if (ghostDuration <= 0f)
        {
            StopTimingGhost();
            return;
        }

        ghostElapsed += deltaTime;
        float progress = Mathf.Clamp01(
            ghostElapsed / ghostDuration);
        timingGhostRenderer.transform.SetPositionAndRotation(
            ghostPosition,
            ghostRotation);
        timingGhostRenderer.transform.localScale =
            ghostScale * Mathf.Lerp(1f, ghostEndScale, progress);

        Color color = ghostColor;
        color.a = ghostColor.a * (1f - progress);
        timingGhostRenderer.color = color;

        if (progress >= 1f)
            StopTimingGhost();
    }

    private void StopTimingGhost()
    {
        ghostElapsed = 0f;
        if (timingGhostRenderer != null)
            timingGhostRenderer.enabled = false;

        transform.localPosition = feedbackLocalPosition;
        transform.localRotation = feedbackLocalRotation;
        transform.localScale = feedbackLocalScale;
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

        AnimatedSprite effect = GetAvailableEffect();
        if (effect == null)
            return;

        effect.GameObject.name = effectName;
        effect.GameObject.SetActive(true);
        effect.InUse = true;
        effect.Age = 0f;
        SpriteRenderer spriteRenderer = effect.Renderer;
        spriteRenderer.enabled = true;
        spriteRenderer.color = color;
        spriteRenderer.sortingLayerID = effectSortingLayerId;
        spriteRenderer.sortingOrder = effectSortingOrder;

        Transform effectTransform = effect.Transform;
        effectTransform.position = startPosition;
        effectTransform.localRotation =
            Quaternion.Euler(0f, 0f, startRotation);

        Vector3 baseScale = GetBaseScaleForSize(size);
        effectTransform.localScale =
            Vector3.Scale(baseScale, startScale);

        effect.StartPosition = startPosition;
        effect.EndPosition = endPosition;
        effect.BaseScale = baseScale;
        effect.StartScale = startScale;
        effect.EndScale = endScale;
        effect.StartRotation = startRotation;
        effect.EndRotation = endRotation;
        effect.Color = color;
        effect.Duration = Mathf.Max(0.001f, duration);
    }

    private void BuildEffectPool()
    {
        activeEffects.Capacity = Mathf.Max(
            activeEffects.Capacity,
            effectPoolSize);
        for (int i = 0; i < effectPoolSize; i++)
        {
            var effectObject = new GameObject($"TimingEffect_{i:00}");
            effectObject.transform.SetParent(transform, false);
            var spriteRenderer =
                effectObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = effectSprite;
            if (effectMaterial != null)
                spriteRenderer.sharedMaterial = effectMaterial;
            spriteRenderer.sortingLayerID = effectSortingLayerId;
            spriteRenderer.sortingOrder = effectSortingOrder;
            spriteRenderer.enabled = false;
            effectObject.SetActive(false);

            activeEffects.Add(new AnimatedSprite
            {
                GameObject = effectObject,
                Transform = effectObject.transform,
                Renderer = spriteRenderer,
                BaseScale = Vector3.one,
                Duration = 0.001f
            });
        }
    }

    private AnimatedSprite GetAvailableEffect()
    {
        AnimatedSprite oldest = null;
        foreach (AnimatedSprite effect in activeEffects)
        {
            if (!effect.InUse)
                return effect;
            if (oldest == null || effect.Age > oldest.Age)
                oldest = effect;
        }

        return oldest;
    }

    private static void ReleaseEffect(AnimatedSprite effect)
    {
        if (effect == null || !effect.InUse)
            return;

        effect.InUse = false;
        effect.Age = 0f;
        effect.Renderer.enabled = false;
        effect.GameObject.SetActive(false);
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
