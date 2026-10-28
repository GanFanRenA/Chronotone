using UnityEngine;

/// <summary>
/// X 轴强制跟随谱面时间轴；Y 轴由本模式自行积分。
/// Kinematic Rigidbody2D 只负责平滑移动和碰撞查询，不受物理力驱动。
/// 空中持续旋转碰撞体；重力反转后同样成立。
/// </summary>
public sealed class PhysicsActionMode : PlayerActionMode
{
    [Header("跳跃物理")]
    [Tooltip("沿重力反方向施加的起跳速度。")]
    [SerializeField, Min(0.1f)] private float jumpVelocity = 8f;
    [SerializeField, Min(0f)] private float gravityScale = 3f;
    [Tooltip("空中旋转角速度，单位为度/秒。")]
    [SerializeField] private float angularSpeed = 220f;
    [Tooltip("速坠期间的重力倍率，范围为普通重力的 2 至 3 倍。")]
    [SerializeField, Range(2f, 3f)] private float fastFallGravityMultiplier = 2.5f;

    [Header("地面")]
    [SerializeField] private LayerMask groundLayers = ~0;

    [Header("阻挡检测")]
    [Tooltip("用于检测横向撞墙的层。地面正下方接触不会触发阻挡。")]
    [SerializeField] private LayerMask blockingLayers = 1 << 6;
    [SerializeField, Min(0.01f)] private float blockedCheckWindow = 0.12f;
    [SerializeField, Min(0.0001f)] private float minMoveDistance = 0.001f;
    [SerializeField, Min(0f)] private float collisionSkin = 0.002f;

    [Header("落地检测")]
    [SerializeField, Min(0.001f)] private float groundProbeDistance = 0.03f;

    private Rigidbody2D body;
    private BoxCollider2D bodyCollider;
    private ChartMotionTimeline timeline;
    private Vector2 respawnPosition;
    private float blockedElapsed;
    private int blockedDirection;
    private bool jumpQueued;
    private bool grounded;
    private bool gravityInverted;
    private bool fastFallActive;
    private bool respawnStartPending;
    private float verticalSpeed;
    private float spinAngle;
    private ContactFilter2D groundFilter;
    private ContactFilter2D blockingFilter;
    private ContactFilter2D solidFilter;
    private readonly RaycastHit2D[] castHits = new RaycastHit2D[16];
    public float JumpVelocity => jumpVelocity;
    public float GravityScale => gravityScale;
    public float FastFallGravityMultiplier => fastFallGravityMultiplier;
    public float AngularSpeed => angularSpeed;
    public LayerMask GroundLayers => groundLayers;
    public LayerMask BlockingLayers => blockingLayers;
    public float BlockedCheckWindow => blockedCheckWindow;
    public float MinMoveDistance => minMoveDistance;
    public float CollisionSkin => collisionSkin;
    public float GroundProbeDistance => groundProbeDistance;

    public override string DisplayName => "物理移动";

    void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<BoxCollider2D>();

        if (body == null)
            Debug.LogError("物理模式需要 Player 上的 Rigidbody2D", this);

        if (bodyCollider == null)
            Debug.LogError("物理模式需要 Player 上的 Collider2D", this);

        if (body != null)
        {
            body.bodyType = RigidbodyType2D.Kinematic;
            body.constraints = RigidbodyConstraints2D.None;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.simulated = false;
        }

        ConfigureCollisionFilters();
    }

    protected override void OnModeEnter()
    {
        if (body == null || bodyCollider == null)
            return;

        timeline = new ChartMotionTimeline(
            Player.PlaybackStartPosition,
            Player.PlaybackStartSongTime,
            Player.Nodes,
            Player.Chart.bpmTimeline);

        gravityInverted = false;
        body.simulated = true;

        float targetX = timeline.Sample(Player.SongTime).x;
        body.position = new Vector2(targetX, Player.PlaybackStartPosition.y);
        spinAngle = NormalizeAngle(Player.OriginalRotation.eulerAngles.z);
        SetPlayerRotation(Player.OriginalRotation);
        IgnoreNodeCollisions();

        verticalSpeed = 0f;
        fastFallActive = false;
        grounded = HasSupport(body.position, spinAngle);
        ResetBlockedState();
        jumpQueued = false;
        respawnStartPending = false;
    }

    protected override void OnModeFixedUpdate(double songTime, float fixedDeltaTime)
    {
        if (timeline == null || body == null || bodyCollider == null)
            return;

        if (respawnStartPending)
        {
            body.position = respawnPosition;
            spinAngle = NormalizeAngle(Player.OriginalRotation.eulerAngles.z);
            SetPlayerRotation(Player.OriginalRotation);
            verticalSpeed = 0f;
            grounded = false;

            if (!Player.AudioHasStarted)
            {
                body.simulated = false;
                return;
            }

            body.simulated = true;
            respawnStartPending = false;
            ResetBlockedState();
            return;
        }

        float targetX = timeline.Sample(songTime).x;
        float deltaTime = Mathf.Max(0.0001f, fixedDeltaTime);
        float deltaX = targetX - body.position.x;
        bool horizontallyBlocked = IsMovementBlocked(deltaX);
        if (UpdateBlockedState(
                horizontallyBlocked,
                deltaX,
                deltaTime))
            return;

        Vector2 nextPosition = body.position;
        if (!horizontallyBlocked)
            nextPosition.x = targetX;

        if (jumpQueued)
        {
            BeginPhysicalJump();
            jumpQueued = false;
        }

        if (grounded &&
            !HasSupport(
                new Vector2(nextPosition.x, body.position.y),
                spinAngle))
        {
            grounded = false;
            verticalSpeed = 0f;
        }

        if (grounded)
        {
            verticalSpeed = 0f;
            fastFallActive = false;
            spinAngle = NormalizeAngle(
                Player.OriginalRotation.eulerAngles.z);
        }
        else
        {
            spinAngle = NormalizeAngle(
                spinAngle + angularSpeed * deltaTime);
            float gravityMultiplier =
                fastFallActive ? fastFallGravityMultiplier : 1f;
            verticalSpeed +=
                GravityAccelerationY * gravityMultiplier * deltaTime;

            float verticalDisplacement =
                verticalSpeed * deltaTime;
            ResolveVerticalMotion(
                ref nextPosition,
                ref spinAngle,
                verticalDisplacement);
        }

        body.MoveRotation(spinAngle);
        body.MovePosition(nextPosition);
    }

    protected override void OnModeRequestJump()
    {
        jumpQueued = true;
    }

    protected override void OnModeJudgement(JudgementResult result)
    {
        if (!result.IsSuccessfulHit ||
            Player.Nodes == null ||
            result.NoteIndex < 0 ||
            result.NoteIndex >= Player.Nodes.Count)
            return;

        switch (Player.Nodes[result.NoteIndex].mode)
        {
            case NodeMode.GravityNode:
            case NodeMode.GravityFastFallNode:
                ApplyFastFall(true);
                break;
            case NodeMode.FastFallNode:
                ApplyFastFall(false);
                break;
            case NodeMode.WeakGravityNode:
                gravityInverted = !gravityInverted;
                grounded = false;
                fastFallActive = false;
                break;
            case NodeMode.GravityJumpNode:
                gravityInverted = !gravityInverted;
                BeginPhysicalJump();
                break;
        }
    }

    protected override void OnModeRespawn(
        Vector2 position,
        double checkpointSongTime,
        int checkpointNodeIndex)
    {
        if (Player.Chart == null)
            return;

        timeline = new ChartMotionTimeline(
            position,
            checkpointSongTime,
            Player.Nodes,
            Player.Chart.bpmTimeline);

        gravityInverted = IsGravityInvertedAtCheckpoint(checkpointNodeIndex);
        grounded = false;
        fastFallActive = false;
        verticalSpeed = 0f;

        if (body != null)
        {
            body.position = position;
            spinAngle = NormalizeAngle(Player.OriginalRotation.eulerAngles.z);
            body.simulated = false;
        }

        respawnPosition = position;
        respawnStartPending = true;
        SetPlayerRotation(Player.OriginalRotation);
        jumpQueued = false;
        ResetBlockedState();
    }

    protected override void OnModeControlLockChanged(bool locked)
    {
        if (!locked)
            return;

        jumpQueued = false;
        respawnStartPending = false;
        ResetBlockedState();
        grounded = false;
        fastFallActive = false;
        verticalSpeed = 0f;

        if (body == null)
            return;

        body.simulated = false;
    }

    protected override void OnModeRewindPose(
        Vector2 position,
        Quaternion rotation)
    {
        base.OnModeRewindPose(position, rotation);

        if (body == null)
            return;

        body.position = position;
        spinAngle = NormalizeAngle(rotation.eulerAngles.z);
        body.MoveRotation(rotation.eulerAngles.z);
    }

    protected override void OnModeExit()
    {
        timeline = null;
        jumpQueued = false;
        respawnStartPending = false;
        grounded = false;
        fastFallActive = false;
        verticalSpeed = 0f;
        ResetBlockedState();

        if (body != null)
        {
            gravityInverted = false;
            body.simulated = false;
        }

        Player.transform.localRotation = Player.OriginalRotation;
    }

    private void BeginPhysicalJump()
    {
        grounded = false;
        fastFallActive = false;
        verticalSpeed = SupportNormalY * jumpVelocity;
    }

    private void ApplyFastFall(bool invertGravity)
    {
        if (invertGravity)
            gravityInverted = !gravityInverted;

        grounded = false;
        verticalSpeed = 0f;
        fastFallActive = true;
    }

    private bool UpdateBlockedState(
        bool horizontallyBlocked,
        float deltaX,
        float deltaTime)
    {
        if (Mathf.Abs(deltaX) < minMoveDistance)
        {
            ResetBlockedState();
            return false;
        }

        int direction = deltaX > 0f ? 1 : -1;
        if (blockedDirection != direction)
        {
            ResetBlockedState();
            blockedDirection = direction;
        }

        if (!horizontallyBlocked)
        {
            blockedElapsed = 0f;
            return false;
        }

        blockedElapsed += deltaTime;
        if (blockedElapsed < blockedCheckWindow)
            return false;

        ResetBlockedState();
        Player.ReportBlockedBad();
        return true;
    }

    private void ResetBlockedState()
    {
        blockedElapsed = 0f;
        blockedDirection = 0;
    }

    private void SetPlayerRotation(Quaternion rotation)
    {
        if (body != null)
            body.MoveRotation(rotation.eulerAngles.z);
        else
            Player.transform.localRotation = rotation;
    }

    private bool IsMovementBlocked(float deltaX)
    {
        float distance = Mathf.Abs(deltaX);
        if (distance < minMoveDistance)
            return false;

        Vector2 direction = deltaX > 0f
            ? Vector2.right
            : Vector2.left;
        Vector2 castPosition = body.position;
        if (grounded)
        {
            castPosition += Vector2.up * SupportNormalY *
                (groundProbeDistance + collisionSkin + 0.02f);
        }

        int count = CastAtPose(
            castPosition,
            spinAngle,
            direction,
            distance + collisionSkin,
            blockingFilter);

        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = castHits[i];
            if (IsIgnoredHit(hit))
                continue;

            if (Vector2.Dot(hit.normal, direction) <= -0.55f)
                return true;
        }

        return false;
    }

    private bool HasSupport(Vector2 position, float angle)
    {
        return TryGetSupportHit(
            position,
            angle,
            groundProbeDistance + collisionSkin,
            out _);
    }

    private bool TryGetSupportHit(
        Vector2 position,
        float angle,
        float distance,
        out RaycastHit2D supportHit)
    {
        supportHit = default;

        Vector2 supportNormal =
            Vector2.up * SupportNormalY;
        Vector2 castDirection = -supportNormal;
        float probeLift = Mathf.Max(
            groundProbeDistance,
            collisionSkin * 2f);
        Vector2 probePosition = position + supportNormal * probeLift;
        int count = CastAtPose(
            probePosition,
            angle,
            castDirection,
            distance + probeLift,
            groundFilter);

        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = castHits[i];
            if (IsIgnoredHit(hit) ||
                Vector2.Dot(hit.normal, supportNormal) <= 0.5f)
                continue;

            if (hit.distance >= nearestDistance)
                continue;

            nearestDistance = hit.distance;
            supportHit = hit;
        }

        return nearestDistance < float.PositiveInfinity;
    }

    private void ResolveVerticalMotion(
        ref Vector2 position,
        ref float angle,
        float displacement)
    {
        if (Mathf.Abs(displacement) < 0.00001f)
            return;

        Vector2 direction = displacement > 0f
            ? Vector2.up
            : Vector2.down;
        int count = CastAtPose(
            position,
            angle,
            direction,
            Mathf.Abs(displacement) + collisionSkin,
            solidFilter);

        RaycastHit2D nearestHit = default;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = castHits[i];
            if (IsIgnoredHit(hit) ||
                Vector2.Dot(hit.normal, direction) > -0.1f ||
                hit.distance >= nearestDistance)
                continue;

            nearestDistance = hit.distance;
            nearestHit = hit;
        }

        if (nearestDistance == float.PositiveInfinity)
        {
            position += direction * Mathf.Abs(displacement);
            return;
        }

        float allowedDistance =
            Mathf.Max(0f, nearestDistance - collisionSkin);
        position += direction * allowedDistance;

        bool movingTowardSupport =
            direction.y * SupportNormalY < 0f;
        bool hitSupport =
            Vector2.Dot(
                nearestHit.normal,
                Vector2.up * SupportNormalY) > 0.5f;

        if (movingTowardSupport && hitSupport)
        {
            grounded = true;
            verticalSpeed = 0f;
            LandOnSupport(
                ref position,
                ref angle,
                nearestHit);
            return;
        }

        verticalSpeed = 0f;
    }

    private void LandOnSupport(
        ref Vector2 position,
        ref float angle,
        RaycastHit2D hit)
    {
        angle = NormalizeAngle(
            Player.OriginalRotation.eulerAngles.z);

        Vector2 halfSize = GetColliderWorldSize() * 0.5f;
        float radians = angle * Mathf.Deg2Rad;
        Vector2 localX = new Vector2(
            Mathf.Cos(radians),
            Mathf.Sin(radians));
        Vector2 localY = new Vector2(
            -Mathf.Sin(radians),
            Mathf.Cos(radians));
        float supportExtent =
            Mathf.Abs(Vector2.Dot(hit.normal, localX)) * halfSize.x +
            Mathf.Abs(Vector2.Dot(hit.normal, localY)) * halfSize.y;
        Vector2 colliderOffset =
            GetColliderCenter(position, angle) - position;
        Vector2 uprightCenter =
            hit.point + hit.normal * (supportExtent + collisionSkin);
        position.y = uprightCenter.y - colliderOffset.y;
    }

    private int CastAtPose(
        Vector2 position,
        float angle,
        Vector2 direction,
        float distance,
        ContactFilter2D filter)
    {
        return Physics2D.BoxCast(
            GetColliderCenter(position, angle),
            GetColliderWorldSize(),
            angle,
            direction,
            filter,
            castHits,
            distance);
    }

    private Vector2 GetColliderCenter(
        Vector2 position,
        float angle)
    {
        Vector3 lossyScale = bodyCollider.transform.lossyScale;
        Vector3 scaledOffset = Vector3.Scale(
            bodyCollider.offset,
            lossyScale);
        Vector2 rotatedOffset =
            Quaternion.Euler(0f, 0f, angle) * scaledOffset;
        return position + rotatedOffset;
    }

    private Vector2 GetColliderWorldSize()
    {
        Vector3 lossyScale = bodyCollider.transform.lossyScale;
        return new Vector2(
            Mathf.Abs(bodyCollider.size.x * lossyScale.x),
            Mathf.Abs(bodyCollider.size.y * lossyScale.y));
    }

    private bool IsIgnoredHit(RaycastHit2D hit)
    {
        Collider2D hitCollider = hit.collider;
        if (hitCollider == null || hitCollider.isTrigger)
            return true;

        if (hitCollider == bodyCollider ||
            hitCollider.attachedRigidbody == body)
            return true;

        return hitCollider.GetComponentInParent<NodeMarker>() != null;
    }

    private void ConfigureCollisionFilters()
    {
        groundFilter = new ContactFilter2D
        {
            useTriggers = false,
            useLayerMask = true,
            layerMask = groundLayers.value
        };
        blockingFilter = new ContactFilter2D
        {
            useTriggers = false,
            useLayerMask = true,
            layerMask = blockingLayers.value
        };
        solidFilter = new ContactFilter2D
        {
            useTriggers = false,
            useLayerMask = true,
            layerMask = blockingLayers.value | groundLayers.value
        };
    }

    private float GravityAccelerationY =>
        Physics2D.gravity.y *
        gravityScale *
        (gravityInverted ? -1f : 1f);

    private float SupportNormalY => gravityInverted ? -1f : 1f;

    private static float NormalizeAngle(float angle)
    {
        return Mathf.DeltaAngle(0f, angle);
    }

    private bool IsGravityInvertedAtCheckpoint(int checkpointNodeIndex)
    {
        if (Player.Nodes == null || checkpointNodeIndex < 0)
            return false;

        bool inverted = false;
        int lastIndex = Mathf.Min(checkpointNodeIndex, Player.Nodes.Count - 1);
        for (int i = 0; i <= lastIndex; i++)
        {
            if (Player.Nodes[i].mode == NodeMode.GravityNode ||
                Player.Nodes[i].mode == NodeMode.GravityFastFallNode ||
                Player.Nodes[i].mode == NodeMode.WeakGravityNode ||
                Player.Nodes[i].mode == NodeMode.GravityJumpNode)
                inverted = !inverted;
        }

        return inverted;
    }

    private void IgnoreNodeCollisions()
    {
        if (bodyCollider == null || Player.chartPlayer == null)
            return;

        Transform root = Player.chartPlayer.scanRoot != null
            ? Player.chartPlayer.scanRoot
            : Player.chartPlayer.transform;

        NodeMarker[] markers = root.GetComponentsInChildren<NodeMarker>(false);
        foreach (NodeMarker marker in markers)
        {
            Collider2D[] nodeColliders =
                marker.GetComponentsInChildren<Collider2D>(false);

            foreach (Collider2D nodeCollider in nodeColliders)
                Physics2D.IgnoreCollision(bodyCollider, nodeCollider, true);
        }
    }
}
