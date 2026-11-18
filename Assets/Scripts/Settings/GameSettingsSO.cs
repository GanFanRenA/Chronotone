using UnityEngine;

[CreateAssetMenu(
    menuName = "Rhythm/Game Settings",
    fileName = "GameSettings")]
public sealed class GameSettingsSO : ScriptableObject
{
    public const string DefaultResourcesPath = "GameSettings";

    private static GameSettingsSO defaultSettings;

    [Header("Judgement")]
    [SerializeField, Range(0.02f, 0.2f)]
    private float perfectWindowSeconds = 0.06f;

    [SerializeField, Range(0.04f, 0.3f)]
    private float greatWindowSeconds = 0.14f;

    [SerializeField, Range(0.16f, 0.4f)]
    private float badWindowSeconds = 0.24f;

    [Header("Auto Play")]
    [SerializeField] private bool autoModeEnabled;

    [Tooltip("正值延后，负值提前；运行时会限制在 Great 判定窗内。")]
    [SerializeField, Range(-300, 300)]
    private int autoPlayOffsetMilliseconds;

    [Header("Playback")]
    [SerializeField, Min(0f)] private float scheduleLeadSeconds = 0.15f;
    [SerializeField, Range(0f, 1f)]
    private float timelineNodeClickVolume = 0.8f;

    [Header("Death Rewind")]
    [SerializeField, Min(0f)] private float respawnDelaySeconds = 0.35f;
    [SerializeField, Min(0.01f)] private float rewindDurationSeconds = 0.65f;
    [SerializeField, Min(1)] private int motionSamplesPerSecond = 30;
    [SerializeField, Min(1f)] private float motionHistorySeconds = 30f;
    [SerializeField] private Color rewindTint =
        new Color(0.35f, 0.85f, 1f, 1f);
    [SerializeField, Min(0f)] private float rewindPulseFrequency = 18f;

    [Header("Physics")]
    [Tooltip("Jump 节点的默认起跳速度；节点 strength 为 0 时使用。")]
    [SerializeField, Min(0.1f)] private float jumpVelocity = 8f;
    [SerializeField, Min(0f)] private float gravityScale = 3f;
    [Tooltip("空中旋转角速度，单位为度/秒。")]
    [SerializeField] private float angularSpeed = 220f;
    [Tooltip("FastFall 节点的默认向下速度脉冲；节点 strength 为 0 时使用。")]
    [SerializeField, Min(0f)]
    private float fastFallImpulse = 60f;
    [SerializeField, Min(1f)] private float fallDeathDistance = 12f;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private LayerMask blockingLayers = 1 << 6;
    [SerializeField, Min(0.01f)] private float blockedCheckWindow = 0.12f;
    [SerializeField, Min(0.0001f)] private float minMoveDistance = 0.001f;
    [SerializeField, Min(0f)] private float collisionSkin = 0.002f;
    [SerializeField, Min(0.001f)] private float groundProbeDistance = 0.03f;

    [Header("Judgement Feedback")]
    [SerializeField, Range(0.1f, 5f)] private float sizeMultiplier = 1f;
    [SerializeField, Min(0.0001f)]
    private float worldUnitsPerReferencePixel = 0.01f;
    [SerializeField, Min(0f)] private float perfectDiamondSize = 42f;
    [SerializeField, Min(0f)] private float greatDiamondSize = 36f;
    [SerializeField, Min(0f)] private float missDiamondSize = 30f;
    [SerializeField, Min(0f)] private float shardSize = 13f;
    [SerializeField, Min(0f)] private float shardDistance = 74f;
    [SerializeField] private Vector2 crossSize = new Vector2(64f, 10f);

    public float PerfectWindowSeconds => perfectWindowSeconds;
    public float GreatWindowSeconds => greatWindowSeconds;
    public float BadWindowSeconds => badWindowSeconds;

    public bool AutoModeEnabled
    {
        get => autoModeEnabled;
        set => autoModeEnabled = value;
    }

    public int AutoPlayOffsetMilliseconds
    {
        get => autoPlayOffsetMilliseconds;
        set
        {
            int limit = Mathf.FloorToInt(greatWindowSeconds * 1000f);
            autoPlayOffsetMilliseconds =
                Mathf.Clamp(value, -limit, limit);
        }
    }

    public float ScheduleLeadSeconds => scheduleLeadSeconds;
    public float TimelineNodeClickVolume => timelineNodeClickVolume;

    public float RespawnDelaySeconds => respawnDelaySeconds;
    public float RewindDurationSeconds => rewindDurationSeconds;
    public int MotionSamplesPerSecond => motionSamplesPerSecond;
    public float MotionHistorySeconds => motionHistorySeconds;
    public Color RewindTint => rewindTint;
    public float RewindPulseFrequency => rewindPulseFrequency;

    public float JumpVelocity => jumpVelocity;
    public float GravityScale => gravityScale;
    public float AngularSpeed => angularSpeed;
    public float FastFallImpulse => fastFallImpulse;
    public float FallDeathDistance => fallDeathDistance;
    public LayerMask GroundLayers => groundLayers;
    public LayerMask BlockingLayers => blockingLayers;
    public float BlockedCheckWindow => blockedCheckWindow;
    public float MinMoveDistance => minMoveDistance;
    public float CollisionSkin => collisionSkin;
    public float GroundProbeDistance => groundProbeDistance;

    public float SizeMultiplier
    {
        get => sizeMultiplier;
        set => sizeMultiplier = value;
    }

    public float WorldUnitsPerReferencePixel =>
        worldUnitsPerReferencePixel;
    public float PerfectDiamondSize => perfectDiamondSize;
    public float GreatDiamondSize => greatDiamondSize;
    public float MissDiamondSize => missDiamondSize;
    public float ShardSize => shardSize;
    public float ShardDistance => shardDistance;
    public Vector2 CrossSize => crossSize;

    public static GameSettingsSO LoadDefault()
    {
        if (defaultSettings != null)
            return defaultSettings;

        defaultSettings =
            Resources.Load<GameSettingsSO>(DefaultResourcesPath);
        if (defaultSettings == null)
        {
            Debug.LogWarning(
                $"未找到 Resources/{DefaultResourcesPath}，GameSettings 将使用内存默认值。");
            defaultSettings = CreateInstance<GameSettingsSO>();
            defaultSettings.hideFlags =
                HideFlags.DontSave | HideFlags.HideAndDontSave;
        }

        defaultSettings.Validate();
        return defaultSettings;
    }

    private void OnValidate()
    {
        Validate();
    }

    public void Validate()
    {
        perfectWindowSeconds =
            Mathf.Clamp(perfectWindowSeconds, 0.02f, 0.2f);
        greatWindowSeconds = Mathf.Clamp(
            Mathf.Max(perfectWindowSeconds, greatWindowSeconds),
            0.04f,
            0.3f);
        badWindowSeconds = Mathf.Clamp(
            Mathf.Max(greatWindowSeconds, badWindowSeconds),
            0.16f,
            0.4f);

        int autoPlayLimit =
            Mathf.FloorToInt(greatWindowSeconds * 1000f);
        autoPlayOffsetMilliseconds = Mathf.Clamp(
            autoPlayOffsetMilliseconds,
            -autoPlayLimit,
            autoPlayLimit);

        scheduleLeadSeconds = Mathf.Max(0f, scheduleLeadSeconds);
        timelineNodeClickVolume =
            Mathf.Clamp01(timelineNodeClickVolume);

        respawnDelaySeconds = Mathf.Max(0f, respawnDelaySeconds);
        rewindDurationSeconds =
            Mathf.Max(0.01f, rewindDurationSeconds);
        motionSamplesPerSecond = Mathf.Max(1, motionSamplesPerSecond);
        motionHistorySeconds = Mathf.Max(1f, motionHistorySeconds);
        rewindPulseFrequency =
            Mathf.Max(0f, rewindPulseFrequency);

        jumpVelocity = Mathf.Max(0.1f, jumpVelocity);
        gravityScale = Mathf.Max(0f, gravityScale);
        fastFallImpulse = Mathf.Max(0f, fastFallImpulse);
        fallDeathDistance = Mathf.Max(1f, fallDeathDistance);
        blockedCheckWindow = Mathf.Max(0.01f, blockedCheckWindow);
        minMoveDistance = Mathf.Max(0.0001f, minMoveDistance);
        collisionSkin = Mathf.Max(0f, collisionSkin);
        groundProbeDistance =
            Mathf.Max(0.001f, groundProbeDistance);

        sizeMultiplier = Mathf.Clamp(sizeMultiplier, 0.1f, 5f);
        worldUnitsPerReferencePixel =
            Mathf.Max(0.0001f, worldUnitsPerReferencePixel);
        perfectDiamondSize = Mathf.Max(0f, perfectDiamondSize);
        greatDiamondSize = Mathf.Max(0f, greatDiamondSize);
        missDiamondSize = Mathf.Max(0f, missDiamondSize);
        shardSize = Mathf.Max(0f, shardSize);
        shardDistance = Mathf.Max(0f, shardDistance);
        crossSize = Vector2.Max(Vector2.zero, crossSize);
    }
}
