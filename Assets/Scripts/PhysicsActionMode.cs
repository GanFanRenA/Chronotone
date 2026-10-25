using UnityEngine;

/// <summary>
/// X 轴强制跟随谱面时间轴；Y 轴完全交给 Rigidbody2D。
/// 跳跃输入沿重力反方向施加物理速度；重力反转后同样成立。
/// </summary>
public sealed class PhysicsActionMode : PlayerActionMode
{
    [Header("跳跃物理")]
    [Tooltip("沿重力反方向施加的起跳速度。")]
    [SerializeField, Min(0.1f)] private float jumpVelocity = 8f;
    [SerializeField, Min(0f)] private float gravityScale = 3f;
    [Tooltip("起跳角速度，单位为度/秒。")]
    [SerializeField] private float angularSpeed = 220f;

    [Header("落地检测")]
    [SerializeField] private LayerMask groundLayers = ~0;

    [Header("阻挡检测")]
    [SerializeField, Min(0.01f)] private float minExpectedSpeed = 0.2f;
    [SerializeField, Range(0.01f, 0.9f)] private float minVelocityRatio = 0.1f;
    [SerializeField, Min(0.01f)] private float blockedCheckWindow = 0.12f;

    private Rigidbody2D body;
    private BoxCollider2D bodyCollider;
    private ChartMotionTimeline timeline;
    private Transform cameraTransform;
    private Quaternion cameraWorldRotation;
    private float jumpAge;
    private Vector2 respawnPosition;
    private float lastTargetX;
    private float blockedElapsed;
    private int blockedDirection;
    private bool jumpQueued;
    private bool jumpActive;
    private bool gravityInverted;
    private bool respawnStartPending;
    private readonly ContactPoint2D[] groundContacts = new ContactPoint2D[16];

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
            body.bodyType = RigidbodyType2D.Dynamic;
            ApplyGravityScale();
            body.constraints &= ~RigidbodyConstraints2D.FreezeRotation;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.simulated = false;
        }
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
        ApplyGravityScale();
        body.simulated = true;

        float targetX = timeline.Sample(Player.SongTime).x;
        body.position = new Vector2(targetX, Player.OriginalPosition.y);
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        SetPlayerRotation(Player.OriginalRotation);
        IgnoreNodeCollisions();

        lastTargetX = targetX;
        ResetBlockedState();
        jumpQueued = false;
        jumpActive = false;
        respawnStartPending = false;
        jumpAge = 0f;
    }

    protected override void OnModeFixedUpdate(double songTime, float fixedDeltaTime)
    {
        if (timeline == null || body == null || bodyCollider == null)
            return;

        if (respawnStartPending)
        {
            body.position = respawnPosition;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;

            if (!Player.AudioHasStarted)
            {
                body.simulated = false;
                return;
            }

            body.simulated = true;
            respawnStartPending = false;
            lastTargetX = timeline.Sample(Player.SongTime).x;
            ResetBlockedState();
            return;
        }

        float targetX = timeline.Sample(songTime).x;
        float deltaTime = Mathf.Max(0.0001f, fixedDeltaTime);
        float expectedVelocityX = (targetX - lastTargetX) / deltaTime;
        if (UpdateBlockedState(
                expectedVelocityX,
                body.linearVelocity.x,
                fixedDeltaTime))
            return;

        Vector2 lockedPosition = body.position;
        lockedPosition.x = targetX;
        body.position = lockedPosition;
        body.linearVelocity = new Vector2(
            expectedVelocityX,
            body.linearVelocity.y);
        lastTargetX = targetX;

        bool grounded = IsGrounded();
        if (grounded && !jumpActive)
            body.linearVelocity = new Vector2(body.linearVelocity.x, 0f);

        if (jumpQueued)
        {
            BeginPhysicalJump();
            jumpQueued = false;
        }

        if (!jumpActive)
            return;

        jumpAge += fixedDeltaTime;
        if (grounded && jumpAge > 0.05f)
        {
            jumpActive = false;
            body.linearVelocity = new Vector2(body.linearVelocity.x, 0f);
        }
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

        if (Player.Nodes[result.NoteIndex].mode != NodeMode.GravityNode)
            return;

        gravityInverted = !gravityInverted;
        ApplyGravityScale();
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
        ApplyGravityScale();

        if (body != null)
        {
            body.position = position;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.simulated = false;
        }

        respawnPosition = position;
        respawnStartPending = true;
        SetPlayerRotation(Player.OriginalRotation);
        jumpQueued = false;
        jumpActive = false;
        lastTargetX = position.x;
        ResetBlockedState();
        jumpAge = 0f;
    }

    protected override void OnModeControlLockChanged(bool locked)
    {
        if (!locked)
            return;

        jumpQueued = false;
        jumpActive = false;
        respawnStartPending = false;
        ResetBlockedState();
        jumpAge = 0f;

        if (body == null)
            return;

        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
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
        body.MoveRotation(rotation.eulerAngles.z);
    }

    protected override void OnModeExit()
    {
        timeline = null;
        jumpQueued = false;
        jumpActive = false;
        respawnStartPending = false;
        lastTargetX = 0f;
        ResetBlockedState();
        jumpAge = 0f;

        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            gravityInverted = false;
            ApplyGravityScale();
            body.simulated = false;
        }

        Player.transform.localRotation = Player.OriginalRotation;

        if (cameraTransform != null)
            cameraTransform.rotation = cameraWorldRotation;

        cameraTransform = null;
    }

    private void BeginPhysicalJump()
    {
        body.linearVelocity = new Vector2(
            body.linearVelocity.x,
            SupportDirectionY * jumpVelocity);
        body.angularVelocity = angularSpeed;
        jumpAge = 0f;
        jumpActive = true;
    }

    private bool UpdateBlockedState(
        float expectedVelocityX,
        float actualVelocityX,
        float deltaTime)
    {
        float expectedSpeed = Mathf.Abs(expectedVelocityX);
        if (expectedSpeed < minExpectedSpeed)
        {
            ResetBlockedState();
            return false;
        }

        int direction = expectedVelocityX > 0f ? 1 : -1;
        if (blockedDirection == 0)
        {
            blockedDirection = direction;
        }
        else if (blockedDirection != direction)
        {
            ResetBlockedState();
            blockedDirection = direction;
        }

        float actualAlongTrack = actualVelocityX * direction;
        if (actualAlongTrack >= expectedSpeed * minVelocityRatio)
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

    private bool IsGrounded()
    {
        int contactCount = bodyCollider.GetContacts(groundContacts);
        for (int i = 0; i < contactCount; i++)
        {
            ContactPoint2D contact = groundContacts[i];
            int layerBit = 1 << contact.collider.gameObject.layer;
            if ((groundLayers.value & layerBit) != 0 &&
                contact.normal.y * SupportDirectionY > 0.5f)
                return true;
        }

        return false;
    }

    private float SupportDirectionY => gravityInverted ? -1f : 1f;

    private void ApplyGravityScale()
    {
        if (body != null)
            body.gravityScale = gravityScale * (gravityInverted ? -1f : 1f);
    }

    private bool IsGravityInvertedAtCheckpoint(int checkpointNodeIndex)
    {
        if (Player.Nodes == null || checkpointNodeIndex < 0)
            return false;

        bool inverted = false;
        int lastIndex = Mathf.Min(checkpointNodeIndex, Player.Nodes.Count - 1);
        for (int i = 0; i <= lastIndex; i++)
        {
            if (Player.Nodes[i].mode == NodeMode.GravityNode)
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
