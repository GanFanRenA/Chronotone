using UnityEngine;

/// <summary>
/// 仅在着地且移动时生成固定数量的白色方块拖尾。
/// 小方块从后侧接地角斜向溅出，不修改 Player 的物理状态。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerGroundTrail : MonoBehaviour
{
    private sealed class TrailSegment
    {
        public GameObject GameObject;
        public SpriteRenderer Renderer;
        public Vector3 WorldPosition;
        public Vector2 Velocity;
        public float GravityY;
        public float Size;
        public float Age;
        public bool Active;
    }

    [Header("References")]
    [SerializeField] private Player player;
    [SerializeField] private PhysicsActionMode physicsMode;
    [SerializeField] private BoxCollider2D bodyCollider;
    [SerializeField] private SpriteRenderer sourceRenderer;

    [Header("Trail")]
    [SerializeField, Min(1)] private int segmentCount = 8;
    [SerializeField, Min(0.01f)] private float spawnDistance = 0.24f;
    [SerializeField, Min(0.01f)] private float lifetime = 0.18f;
    [SerializeField, Min(0.01f)] private float squareSize = 0.16f;
    [SerializeField, Range(0f, 1f)] private float minAlpha;
    [SerializeField, Range(0.01f, 1f)] private float endScale = 0.55f;
    [SerializeField, Min(0f)] private float minimumHorizontalStep = 0.015f;
    [SerializeField, Min(0f)] private float sprayAngleDegrees = 30f;
    [SerializeField, Min(0f)] private float sprayAngleJitterDegrees = 5f;
    [SerializeField, Min(0f)] private float spraySpeedMin = 1.8f;
    [SerializeField, Min(0f)] private float spraySpeedMax = 2.6f;
    [SerializeField, Min(0f)] private float sprayGravity = 3f;
    [SerializeField] private Color trailColor =
        new Color(1f, 1f, 1f, 0.62f);

    private TrailSegment[] segments;
    private Vector3 lastSamplePosition;
    private float lastPlayerX;
    private float lastGroundNormalY;
    private bool hasGroundSample;

    private void Awake()
    {
        if (player == null)
            player = GetComponent<Player>();
        if (player == null)
            player = FindFirstObjectByType<Player>();

        if (physicsMode == null && player != null)
            physicsMode = player.GetComponent<PhysicsActionMode>();
        if (bodyCollider == null && player != null)
            bodyCollider = player.GetComponent<BoxCollider2D>();
        if (sourceRenderer == null && player != null)
        {
            Transform visualRoot = player.transform.Find("VisualRoot");
            sourceRenderer = visualRoot != null
                ? visualRoot.GetComponent<SpriteRenderer>()
                : player.GetComponentInChildren<SpriteRenderer>(true);
        }

        if (player == null ||
            physicsMode == null ||
            bodyCollider == null ||
            sourceRenderer == null)
        {
            Debug.LogError(
                "PlayerGroundTrail 缺少 Player、PhysicsActionMode、" +
                "BoxCollider2D 或角色 SpriteRenderer。",
                this);
            enabled = false;
            return;
        }

        BuildSegments();
    }

    private void OnDisable()
    {
        HideAllSegments();
    }

    private void LateUpdate()
    {
        UpdateSegments(Time.deltaTime);

        bool canEmit =
            player != null &&
            physicsMode != null &&
            player.IsRunning &&
            player.AudioHasStarted &&
            !player.IsControlLocked &&
            physicsMode.IsGrounded;

        if (!canEmit)
        {
            hasGroundSample = false;
            HideAllSegments();
            return;
        }

        float groundNormalY = physicsMode.GroundNormalY;
        if (hasGroundSample &&
            !Mathf.Approximately(lastGroundNormalY, groundNormalY))
        {
            HideAllSegments();
            hasGroundSample = false;
        }

        Bounds bounds = bodyCollider.bounds;
        int movementDirection =
            bounds.center.x < lastPlayerX ? -1 : 1;
        Vector3 contactPosition = GetGroundContactPosition(
            movementDirection);
        if (!hasGroundSample)
        {
            lastSamplePosition = contactPosition;
            lastPlayerX = bounds.center.x;
            lastGroundNormalY = groundNormalY;
            hasGroundSample = true;
            return;
        }

        Vector3 sampleDelta = contactPosition - lastSamplePosition;
        if (Mathf.Abs(sampleDelta.x) < minimumHorizontalStep ||
            sampleDelta.sqrMagnitude < spawnDistance * spawnDistance)
        {
            return;
        }

        SpawnSegment(
            contactPosition,
            movementDirection,
            groundNormalY);
        lastSamplePosition = contactPosition;
        lastPlayerX = bounds.center.x;
        lastGroundNormalY = groundNormalY;
    }

    private Vector3 GetGroundContactPosition(int movementDirection)
    {
        Bounds bounds = bodyCollider.bounds;
        float normalY = Mathf.Sign(physicsMode.GroundNormalY);
        if (Mathf.Approximately(normalY, 0f))
            normalY = 1f;
        float halfSize = squareSize * 0.5f;
        float backSign = movementDirection < 0 ? 1f : -1f;
        float edgeX = backSign < 0f
            ? bounds.min.x - halfSize
            : bounds.max.x + halfSize;
        float groundY =
            bounds.center.y - normalY * bounds.extents.y;
        float spawnY = groundY + normalY * halfSize;

        return new Vector3(
            edgeX,
            spawnY,
            bounds.center.z);
    }

    private void BuildSegments()
    {
        SpriteRenderer[] renderers =
            GetComponentsInChildren<SpriteRenderer>(true);
        int count = Mathf.Min(segmentCount, renderers.Length);
        if (count <= 0)
        {
            Debug.LogError(
                "PlayerGroundTrail 需要预先放置白色方块 SpriteRenderer 子物体。",
                this);
            enabled = false;
            return;
        }

        segments = new TrailSegment[count];
        for (int i = 0; i < count; i++)
        {
            SpriteRenderer renderer = renderers[i];
            renderer.sprite = sourceRenderer.sprite;
            renderer.sharedMaterial = sourceRenderer.sharedMaterial;
            renderer.sortingLayerID = sourceRenderer.sortingLayerID;
            renderer.sortingOrder = sourceRenderer.sortingOrder - 1;
            renderer.enabled = false;
            renderer.gameObject.SetActive(false);

            segments[i] = new TrailSegment
            {
                GameObject = renderer.gameObject,
                Renderer = renderer
            };
        }
    }

    private void SpawnSegment(
        Vector3 worldPosition,
        int movementDirection,
        float groundNormalY)
    {
        TrailSegment segment = FindAvailableSegment();
        if (segment == null)
            return;

        float minimumSpeed = Mathf.Min(
            spraySpeedMin,
            spraySpeedMax);
        float maximumSpeed = Mathf.Max(
            spraySpeedMin,
            spraySpeedMax);
        float angle = (
            sprayAngleDegrees +
            Random.Range(
                -sprayAngleJitterDegrees,
                sprayAngleJitterDegrees)) *
            Mathf.Deg2Rad;
        float backSign = movementDirection < 0 ? 1f : -1f;
        float normalSign = Mathf.Sign(groundNormalY);
        if (Mathf.Approximately(normalSign, 0f))
            normalSign = 1f;
        Vector2 sprayDirection = new Vector2(
            backSign * Mathf.Cos(angle),
            normalSign * Mathf.Sin(angle));
        float speed = Random.Range(minimumSpeed, maximumSpeed);

        segment.Active = true;
        segment.Age = 0f;
        segment.WorldPosition = worldPosition;
        segment.Velocity = sprayDirection.normalized * speed;
        segment.GravityY = -normalSign * sprayGravity;
        segment.Size = squareSize;
        segment.GameObject.SetActive(true);
        segment.Renderer.enabled = true;
        segment.Renderer.color = trailColor;
        segment.Renderer.transform.SetPositionAndRotation(
            worldPosition,
            Quaternion.identity);
        segment.Renderer.transform.localScale =
            Vector3.one * squareSize;
    }

    private TrailSegment FindAvailableSegment()
    {
        TrailSegment oldest = null;
        foreach (TrailSegment segment in segments)
        {
            if (!segment.Active)
                return segment;
            if (oldest == null || segment.Age > oldest.Age)
                oldest = segment;
        }

        return oldest;
    }

    private void UpdateSegments(float deltaTime)
    {
        if (segments == null)
            return;

        foreach (TrailSegment segment in segments)
        {
            if (!segment.Active)
                continue;

            segment.Age += Mathf.Max(0f, deltaTime);
            float progress = Mathf.Clamp01(segment.Age / lifetime);
            segment.Velocity.y += segment.GravityY * deltaTime;
            segment.WorldPosition +=
                (Vector3)(segment.Velocity * deltaTime);
            segment.Renderer.transform.SetPositionAndRotation(
                segment.WorldPosition,
                Quaternion.identity);
            segment.Renderer.transform.localScale =
                Vector3.one *
                Mathf.Lerp(segment.Size, segment.Size * endScale, progress);

            Color color = trailColor;
            color.a = Mathf.Lerp(
                trailColor.a,
                minAlpha,
                progress);
            segment.Renderer.color = color;

            if (progress >= 1f)
                DeactivateSegment(segment);
        }
    }

    private void HideAllSegments()
    {
        if (segments == null)
            return;

        foreach (TrailSegment segment in segments)
            DeactivateSegment(segment);
    }

    private static void DeactivateSegment(TrailSegment segment)
    {
        segment.Active = false;
        segment.Age = 0f;
        segment.Renderer.enabled = false;
        segment.GameObject.SetActive(false);
    }
}
