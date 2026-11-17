using UnityEngine;

/// <summary>
/// 有轨道权限时严格跟随唯一完美轨道；失去权限后才运行自由物理。
/// 节点模式只影响轨道形状，不再在运行时执行角色动作。
/// </summary>
public sealed class PhysicsActionMode : PlayerActionMode
{
    [System.NonSerialized] private float jumpVelocity;
    [System.NonSerialized] private float gravityScale;
    [System.NonSerialized] private float angularSpeed;
    [System.NonSerialized] private float fastFallGravityMultiplier;
    [System.NonSerialized] private float fallDeathDistance;
    [System.NonSerialized] private LayerMask groundLayers;
    [System.NonSerialized] private LayerMask blockingLayers;
    [System.NonSerialized] private float blockedCheckWindow;
    [System.NonSerialized] private float minMoveDistance;
    [System.NonSerialized] private float collisionSkin;
    [System.NonSerialized] private float groundProbeDistance;
    [System.NonSerialized] private bool settingsApplied;

    private Rigidbody2D body;
    private BoxCollider2D bodyCollider;
    private PerfectRoute perfectRoute;
    private float blockedElapsed;
    private int blockedDirection;
    private bool grounded;
    private bool gravityInverted;
    private bool fastFallActive;
    private bool wasTrackEnabled;
    private bool freeFallOriginValid;
    private float freeFallOriginY;
    private float horizontalSpeed;
    private float verticalSpeed;
    private float spinAngle;
    private ContactFilter2D groundFilter;
    private ContactFilter2D blockingFilter;
    private ContactFilter2D solidFilter;
    private readonly RaycastHit2D[] castHits = new RaycastHit2D[16];

    public float JumpVelocity
    {
        get
        {
            EnsureSettings();
            return jumpVelocity;
        }
    }

    public float GravityScale
    {
        get
        {
            EnsureSettings();
            return gravityScale;
        }
    }

    public float FastFallGravityMultiplier
    {
        get
        {
            EnsureSettings();
            return fastFallGravityMultiplier;
        }
    }

    public float AngularSpeed
    {
        get
        {
            EnsureSettings();
            return angularSpeed;
        }
    }

    public LayerMask GroundLayers
    {
        get
        {
            EnsureSettings();
            return groundLayers;
        }
    }

    public LayerMask BlockingLayers
    {
        get
        {
            EnsureSettings();
            return blockingLayers;
        }
    }

    public float BlockedCheckWindow
    {
        get
        {
            EnsureSettings();
            return blockedCheckWindow;
        }
    }

    public float MinMoveDistance
    {
        get
        {
            EnsureSettings();
            return minMoveDistance;
        }
    }

    public float CollisionSkin
    {
        get
        {
            EnsureSettings();
            return collisionSkin;
        }
    }

    public float GroundProbeDistance
    {
        get
        {
            EnsureSettings();
            return groundProbeDistance;
        }
    }

    public override string DisplayName => "物理移动";

    private void Awake()
    {
        EnsureSettings();
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
            body.collisionDetectionMode =
                CollisionDetectionMode2D.Continuous;
            body.simulated = false;
        }

        ConfigureCollisionFilters();
    }

    private void EnsureSettings()
    {
        if (settingsApplied)
            return;

        Player owner = GetComponent<Player>();
        GameSettingsSO settings = owner != null
            ? owner.GameSettings
            : GameSettingsSO.LoadDefault();
        jumpVelocity = settings.JumpVelocity;
        gravityScale = settings.GravityScale;
        angularSpeed = settings.AngularSpeed;
        fastFallGravityMultiplier =
            settings.FastFallGravityMultiplier;
        fallDeathDistance = settings.FallDeathDistance;
        groundLayers = settings.GroundLayers;
        blockingLayers = settings.BlockingLayers;
        blockedCheckWindow = settings.BlockedCheckWindow;
        minMoveDistance = settings.MinMoveDistance;
        collisionSkin = settings.CollisionSkin;
        groundProbeDistance = settings.GroundProbeDistance;
        settingsApplied = true;
    }

    protected override void OnModeEnter()
    {
        if (body == null || bodyCollider == null || Player.Chart == null)
            return;

        EnsurePerfectRoute();

        IgnoreNodeCollisions();
        ResetMotionState();

        if (!TryApplyTrackState(
                Player.SongTime,
                true,
                out _))
        {
            SetBodyPose(
                Player.PlaybackStartPosition,
                Player.OriginalRotation.eulerAngles.z);
        }

        body.simulated = Player.AudioHasStarted &&
            !Player.IsTrackEnabled;
        wasTrackEnabled = Player.IsTrackEnabled;
    }

    protected override void OnModePrepareForPlayback()
    {
        if (body == null || bodyCollider == null || Player.Chart == null)
            return;

        EnsurePerfectRoute();
    }

    protected override void OnModeFixedUpdate(
        double songTime,
        float fixedDeltaTime)
    {
        if (body == null || bodyCollider == null)
            return;

        if (Player.IsTrackEnabled)
        {
            TickTracked(songTime, fixedDeltaTime);
        }
        else
        {
            if (wasTrackEnabled)
                BeginFreeMotion();

            TickFree(fixedDeltaTime);
        }

        wasTrackEnabled = Player.IsTrackEnabled;
    }

    protected override void OnModeRespawn(
        Vector2 position,
        double checkpointSongTime,
        int checkpointNodeIndex)
    {
        if (body == null)
            return;

        ResetMotionState();
        SetBodyPose(
            position,
            Player.OriginalRotation.eulerAngles.z);
        body.simulated = false;
        wasTrackEnabled = Player.IsTrackEnabled;
    }

    protected override void OnModeControlLockChanged(bool locked)
    {
        ResetMotionState();

        if (body != null)
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
        body.rotation = spinAngle;
        body.simulated = false;
    }

    protected override void OnModeExit()
    {
        ResetMotionState();

        if (body != null)
            body.simulated = false;

        Player.transform.localRotation = Player.OriginalRotation;
    }

    private void TickTracked(double songTime, float fixedDeltaTime)
    {
        float deltaTime = Mathf.Max(0.0001f, fixedDeltaTime);
        if (!TryApplyTrackState(
                songTime,
                true,
                out PerfectRouteSample sample))
        {
            body.simulated = false;
            return;
        }

        if (perfectRoute.TrySampleAt(
                songTime - deltaTime,
                out PerfectRouteSample previousSample))
        {
            horizontalSpeed =
                (sample.Position.x - previousSample.Position.x) /
                deltaTime;
        }

        // 跟轨期间轨道位置是唯一权威，不需要 Physics2D 再推进一次刚体。
        body.simulated = false;
        ResetBlockedState();
    }

    private void BeginFreeMotion()
    {
        double releaseTime = Player.TrackReleaseSongTime;
        int nodeIndex = Player.TrackReleaseNodeIndex;

        if (nodeIndex >= 0 &&
            Player.Nodes != null &&
            nodeIndex < Player.Nodes.Count &&
            Player.Nodes[nodeIndex] != null &&
            Player.Chart != null &&
            Player.Chart.bpmTimeline != null)
        {
            double nodeTime =
                Player.Chart.bpmTimeline.BeatToSeconds(
                    Player.Nodes[nodeIndex].beat);
            if (releaseTime + 0.00001d >= nodeTime)
                releaseTime = nodeTime - 0.00001d;
        }

        if (TryApplyTrackState(
                releaseTime,
                false,
                out PerfectRouteSample releaseSample) &&
            Player.Chart != null &&
            Time.fixedDeltaTime > 0f)
        {
            double previousTime =
                releaseTime - Time.fixedDeltaTime;
            if (perfectRoute.TrySampleAt(
                    previousTime,
                    out PerfectRouteSample previousSample))
            {
                horizontalSpeed =
                    (releaseSample.Position.x -
                     previousSample.Position.x) /
                    Time.fixedDeltaTime;
            }

            freeFallOriginValid = true;
            freeFallOriginY = releaseSample.Position.y;
        }

        body.simulated = true;
        ResetBlockedState();
    }

    private void TickFree(float fixedDeltaTime)
    {
        float deltaTime = Mathf.Max(0.0001f, fixedDeltaTime);
        float deltaX = horizontalSpeed * deltaTime;
        bool horizontallyBlocked = IsMovementBlocked(deltaX);
        if (UpdateBlockedState(
                horizontallyBlocked,
                deltaX,
                deltaTime))
        {
            return;
        }

        Vector2 nextPosition = body.position;
        if (!horizontallyBlocked)
            nextPosition.x += deltaX;

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

            ResolveVerticalMotion(
                ref nextPosition,
                ref spinAngle,
                verticalSpeed * deltaTime);
        }

        if (freeFallOriginValid &&
            (nextPosition.y - freeFallOriginY) * SupportNormalY <
            -fallDeathDistance)
        {
            Player.ReportBlockedDeath();
            return;
        }

        body.MoveRotation(spinAngle);
        body.MovePosition(nextPosition);
    }

    private bool TryApplyTrackState(
        double songTime,
        bool updateBody,
        out PerfectRouteSample sample)
    {
        sample = default;
        if (perfectRoute == null ||
            !perfectRoute.TrySampleAt(
                songTime,
                out sample))
        {
            return false;
        }

        spinAngle = sample.Angle;
        verticalSpeed = sample.VerticalSpeed;
        grounded = sample.Grounded;
        gravityInverted = sample.GravityInverted;
        fastFallActive = sample.FastFallActive;

        if (updateBody && body != null)
            SetBodyPose(sample.Position, sample.Angle);

        return true;
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
        Player.ReportBlockedDeath();
        return true;
    }

    private void ResetBlockedState()
    {
        blockedElapsed = 0f;
        blockedDirection = 0;
    }

    private void ResetMotionState()
    {
        horizontalSpeed = 0f;
        verticalSpeed = 0f;
        grounded = false;
        gravityInverted = false;
        fastFallActive = false;
        freeFallOriginValid = false;
        spinAngle = NormalizeAngle(
            Player.OriginalRotation.eulerAngles.z);
        ResetBlockedState();
    }

    private void SetBodyPose(Vector2 position, float angle)
    {
        if (body != null)
        {
            body.position = position;
            body.rotation = angle;
        }

        Vector3 worldPosition = Player.transform.position;
        worldPosition.x = position.x;
        worldPosition.y = position.y;
        Player.transform.SetPositionAndRotation(
            worldPosition,
            Quaternion.Euler(0f, 0f, angle));
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
                Vector2.Dot(hit.normal, supportNormal) <= 0.5f ||
                hit.distance >= nearestDistance)
            {
                continue;
            }

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
            {
                continue;
            }

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
            fastFallActive = false;
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
        {
            return true;
        }

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

    private void EnsurePerfectRoute()
    {
        if (perfectRoute != null)
            return;

        ChartTrackSO track = Player.Track;
        if (track == null)
        {
            Debug.LogError(
                "Player 的 Stage 没有绑定 ChartTrack，角色无法移动",
                this);
            return;
        }

        perfectRoute = track.CreateRoute();
        if (perfectRoute == null || perfectRoute.Count == 0)
        {
            perfectRoute = null;
            Debug.LogError(
                "ChartTrack 没有可用于开局的轨道样本，请重新烘焙轨道",
                this);
            return;
        }

        if (!perfectRoute.IsComplete || perfectRoute.Blocked)
        {
            Debug.LogWarning(
                $"ChartTrack 未完整生成，将使用现有 {perfectRoute.Count} 个样本继续跟轨",
                this);
        }

        PerfectRouteSample first = perfectRoute.Samples[0];
        bool anchorMatches =
            Player.Chart != null &&
            System.Math.Abs(
                first.Time + Player.Chart.GlobalOffsetSeconds) <=
            0.00001d &&
            Vector2.SqrMagnitude(
                first.Position - Player.OriginalPosition) <=
            0.000001f;
        if (!anchorMatches)
        {
            Debug.LogError(
                "ChartTrack 首点必须对应角色起点和音乐起始时刻，请重新烘焙轨道",
                this);
            perfectRoute = null;
            return;
        }

        Debug.Log(
            $"[轨道] 已接入 ChartTrack：{perfectRoute.Count} 个样本，首点 {first.Position}",
            this);
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

    private static float NormalizeAngle(float angle)
    {
        return Mathf.DeltaAngle(0f, angle);
    }
}
