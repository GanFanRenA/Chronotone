using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct PerfectRouteConfig
{
    public PerfectRouteConfig(PhysicsActionMode mode, float uprightAngle)
    {
        JumpVelocity = mode.JumpVelocity;
        GravityScale = mode.GravityScale;
        FastFallGravityMultiplier = mode.FastFallGravityMultiplier;
        AngularSpeed = mode.AngularSpeed;
        GroundLayers = mode.GroundLayers;
        BlockingLayers = mode.BlockingLayers;
        BlockedCheckWindow = mode.BlockedCheckWindow;
        MinMoveDistance = mode.MinMoveDistance;
        CollisionSkin = mode.CollisionSkin;
        GroundProbeDistance = mode.GroundProbeDistance;
        UprightAngle = Mathf.DeltaAngle(0f, uprightAngle);
    }

    public float JumpVelocity { get; }
    public float GravityScale { get; }
    public float FastFallGravityMultiplier { get; }
    public float AngularSpeed { get; }
    public LayerMask GroundLayers { get; }
    public LayerMask BlockingLayers { get; }
    public float BlockedCheckWindow { get; }
    public float MinMoveDistance { get; }
    public float CollisionSkin { get; }
    public float GroundProbeDistance { get; }
    public float UprightAngle { get; }
}

[Serializable]
public struct PerfectRouteSample
{
    [SerializeField] private double time;
    [SerializeField] private Vector2 position;
    [SerializeField] private float verticalSpeed;
    [SerializeField] private float angle;
    [SerializeField] private bool grounded;
    [SerializeField] private bool gravityInverted;
    [SerializeField] private bool fastFallActive;
    [SerializeField] private float gravityAccelerationY;

    public PerfectRouteSample(
        double time,
        Vector2 position,
        float verticalSpeed,
        float angle,
        bool grounded,
        bool gravityInverted,
        bool fastFallActive,
        float gravityAccelerationY)
    {
        this.time = time;
        this.position = position;
        this.verticalSpeed = verticalSpeed;
        this.angle = angle;
        this.grounded = grounded;
        this.gravityInverted = gravityInverted;
        this.fastFallActive = fastFallActive;
        this.gravityAccelerationY = gravityAccelerationY;
    }

    public double Time => time;
    public Vector2 Position => position;
    public float VerticalSpeed => verticalSpeed;
    public float Angle => angle;
    public bool Grounded => grounded;
    public bool GravityInverted => gravityInverted;
    public bool FastFallActive => fastFallActive;
    public float GravityAccelerationY => gravityAccelerationY;
}

public sealed class PerfectRoute
{
    private readonly PerfectRouteSample[] samples;

    internal PerfectRoute(
        PerfectRouteSample[] samples,
        bool blocked,
        bool complete)
    {
        this.samples = samples ?? Array.Empty<PerfectRouteSample>();
        Blocked = blocked;
        IsComplete = complete;
    }

    public IReadOnlyList<PerfectRouteSample> Samples => samples;
    public int Count => samples.Length;
    public bool Blocked { get; }
    public bool IsComplete { get; }

    public bool TrySampleAt(double songTime, out PerfectRouteSample sample)
    {
        if (samples.Length == 0)
        {
            sample = default;
            return false;
        }

        if (songTime <= samples[0].Time)
        {
            sample = samples[0];
            return true;
        }

        if (songTime >= samples[samples.Length - 1].Time)
        {
            sample = samples[samples.Length - 1];
            return songTime <= samples[samples.Length - 1].Time + 0.00001d;
        }

        int upper = FindUpperIndex(songTime);
        PerfectRouteSample from = samples[upper - 1];
        PerfectRouteSample to = samples[upper];
        double span = to.Time - from.Time;
        if (span <= 0.0000001d)
        {
            sample = to;
            return true;
        }

        float progress = Mathf.Clamp01(
            (float)((songTime - from.Time) / span));
        sample = new PerfectRouteSample(
            songTime,
            Vector2.Lerp(from.Position, to.Position, progress),
            Mathf.Lerp(from.VerticalSpeed, to.VerticalSpeed, progress),
            Mathf.LerpAngle(from.Angle, to.Angle, progress),
            from.Grounded,
            from.GravityInverted,
            from.FastFallActive,
            from.GravityAccelerationY);
        return true;
    }

    private int FindUpperIndex(double songTime)
    {
        int low = 1;
        int high = samples.Length - 1;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (samples[middle].Time <= songTime)
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }
}

public static class PerfectRouteBuilder
{
    private const int MaxSimulationSteps = 1000000;
    private const double TimeEpsilon = 0.00001d;

    public static PerfectRoute Build(
        Vector2 startPosition,
        double startSongTime,
        IReadOnlyList<NodeData> nodes,
        BpmTimeline bpmTimeline,
        PerfectRouteConfig config,
        BoxCollider2D bodyCollider,
        Rigidbody2D body,
        double endSongTime)
    {
        if (bpmTimeline == null || bodyCollider == null)
            return null;

        Physics2D.SyncTransforms();
        double fixedDeltaTime = Mathf.Max(0.0001f, Time.fixedDeltaTime);
        endSongTime = Math.Max(startSongTime, endSongTime);

        bool gravityInverted = false;
        var actions = new List<RouteAction>();
        if (nodes != null)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeData node = nodes[i];
                if (node == null)
                    continue;

                double noteTime = bpmTimeline.BeatToSeconds(node.beat);
                if (noteTime < startSongTime - TimeEpsilon)
                {
                    ToggleGravity(ref gravityInverted, node.mode);
                    continue;
                }

                actions.Add(new RouteAction(noteTime, node.mode));
            }
        }

        var samples = new List<PerfectRouteSample>();
        var xTimeline = new RouteXTimeline(
            startPosition,
            startSongTime,
            nodes,
            bpmTimeline);
        var state = new RouteState
        {
            Position = startPosition,
            Angle = config.UprightAngle,
            GravityInverted = gravityInverted
        };
        var physics = new RoutePhysics(
            config,
            bodyCollider,
            body,
            state);
        state.Grounded = physics.HasSupport(
            state.Position,
            state.Angle);

        int nextActionIndex = 0;
        while (nextActionIndex < actions.Count &&
               actions[nextActionIndex].Time <= startSongTime + TimeEpsilon)
        {
            ApplyNodeAction(
                state,
                config,
                actions[nextActionIndex].Mode);
            nextActionIndex++;
        }

        samples.Add(ToSample(startSongTime, state, config));

        bool blocked = false;
        bool complete = endSongTime <= startSongTime + TimeEpsilon;
        double previousTickTime = startSongTime;
        int step = 1;
        while (!complete && step <= MaxSimulationSteps)
        {
            double tickTime =
                startSongTime + step * fixedDeltaTime;
            if (tickTime > endSongTime + TimeEpsilon)
            {
                tickTime = endSongTime;
                complete = true;
            }

            float targetX = xTimeline.Sample((float)tickTime);
            float deltaTime =
                (float)(tickTime - previousTickTime);
            if (physics.Step(targetX, deltaTime))
            {
                blocked = true;
                break;
            }

            while (nextActionIndex < actions.Count &&
                   actions[nextActionIndex].Time <=
                   tickTime + TimeEpsilon)
            {
                ApplyNodeAction(
                    state,
                    config,
                    actions[nextActionIndex].Mode);
                nextActionIndex++;
            }

            samples.Add(ToSample(tickTime, state, config));
            previousTickTime = tickTime;
            if (tickTime >= endSongTime - TimeEpsilon)
                complete = true;
            step++;
        }

        return new PerfectRoute(samples.ToArray(), blocked, complete);
    }

    private static PerfectRouteSample ToSample(
        double time,
        RouteState state,
        PerfectRouteConfig config)
    {
        float multiplier =
            state.FastFallActive
                ? config.FastFallGravityMultiplier
                : 1f;
        float gravityAcceleration =
            Physics2D.gravity.y *
            config.GravityScale *
            (state.GravityInverted ? -1f : 1f) *
            multiplier;

        return new PerfectRouteSample(
            time,
            state.Position,
            state.VerticalSpeed,
            state.Angle,
            state.Grounded,
            state.GravityInverted,
            state.FastFallActive,
            gravityAcceleration);
    }

    private static void ApplyNodeAction(
        RouteState state,
        PerfectRouteConfig config,
        NodeMode mode)
    {
        float supportNormalY = state.GravityInverted ? -1f : 1f;
        switch (mode)
        {
            case NodeMode.JumpNode:
                BeginJump(state, config, supportNormalY);
                break;
            case NodeMode.GravityNode:
            case NodeMode.GravityFastFallNode:
                state.GravityInverted = !state.GravityInverted;
                BeginFastFall(state);
                break;
            case NodeMode.FastFallNode:
                BeginFastFall(state);
                break;
            case NodeMode.WeakGravityNode:
                state.GravityInverted = !state.GravityInverted;
                state.Grounded = false;
                state.FastFallActive = false;
                break;
            case NodeMode.GravityJumpNode:
                state.GravityInverted = !state.GravityInverted;
                supportNormalY = state.GravityInverted ? -1f : 1f;
                BeginJump(state, config, supportNormalY);
                break;
        }
    }

    private static void BeginJump(
        RouteState state,
        PerfectRouteConfig config,
        float supportNormalY)
    {
        state.Grounded = false;
        state.FastFallActive = false;
        state.VerticalSpeed = supportNormalY * config.JumpVelocity;
    }

    private static void BeginFastFall(RouteState state)
    {
        state.Grounded = false;
        state.VerticalSpeed = 0f;
        state.FastFallActive = true;
    }

    private static void ToggleGravity(
        ref bool gravityInverted,
        NodeMode mode)
    {
        if (mode == NodeMode.GravityNode ||
            mode == NodeMode.GravityFastFallNode ||
            mode == NodeMode.WeakGravityNode ||
            mode == NodeMode.GravityJumpNode)
        {
            gravityInverted = !gravityInverted;
        }
    }

    private readonly struct RouteAction
    {
        public RouteAction(double time, NodeMode mode)
        {
            Time = time;
            Mode = mode;
        }

        public double Time { get; }
        public NodeMode Mode { get; }
    }

    private sealed class RouteState
    {
        public Vector2 Position;
        public float VerticalSpeed;
        public float Angle;
        public bool Grounded;
        public bool GravityInverted;
        public bool FastFallActive;
        public float BlockedElapsed;
        public int BlockedDirection;
    }

    private sealed class RouteXTimeline
    {
        private readonly double[] times;
        private readonly float[] positions;

        public RouteXTimeline(
            Vector2 startPosition,
            double startSongTime,
            IReadOnlyList<NodeData> nodes,
            BpmTimeline bpmTimeline)
        {
            var routeTimes = new List<double>();
            var routePositions = new List<float>();
            routeTimes.Add(startSongTime);
            routePositions.Add(startPosition.x);

            if (nodes != null)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    NodeData node = nodes[i];
                    if (node == null)
                        continue;

                    double noteTime =
                        bpmTimeline.BeatToSeconds(node.beat);
                    if (noteTime <= startSongTime + TimeEpsilon)
                        continue;

                    routeTimes.Add(noteTime);
                    routePositions.Add(node.position.x);
                }
            }

            times = routeTimes.ToArray();
            positions = routePositions.ToArray();
        }

        public float Sample(float songTime)
        {
            if (times.Length == 0)
                return 0f;
            if (songTime <= times[0])
                return positions[0];
            if (songTime >= times[times.Length - 1])
                return positions[positions.Length - 1];

            int low = 1;
            int high = times.Length - 1;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (times[middle] <= songTime)
                    low = middle + 1;
                else
                    high = middle;
            }

            int upper = low;
            int lower = upper - 1;
            double span = times[upper] - times[lower];
            if (span <= TimeEpsilon)
                return positions[upper];

            float progress = Mathf.Clamp01(
                (float)((songTime - times[lower]) / span));
            return Mathf.Lerp(
                positions[lower],
                positions[upper],
                progress);
        }
    }

    private sealed class RoutePhysics
    {
        private readonly PerfectRouteConfig config;
        private readonly BoxCollider2D bodyCollider;
        private readonly Rigidbody2D body;
        private readonly ContactFilter2D groundFilter;
        private readonly ContactFilter2D blockingFilter;
        private readonly ContactFilter2D solidFilter;
        private readonly RaycastHit2D[] castHits =
            new RaycastHit2D[16];
        private readonly RouteState state;

        public RoutePhysics(
            PerfectRouteConfig config,
            BoxCollider2D bodyCollider,
            Rigidbody2D body,
            RouteState state)
        {
            this.config = config;
            this.bodyCollider = bodyCollider;
            this.body = body;
            this.state = state;

            groundFilter = new ContactFilter2D
            {
                useTriggers = false,
                useLayerMask = true,
                layerMask = config.GroundLayers.value
            };
            blockingFilter = new ContactFilter2D
            {
                useTriggers = false,
                useLayerMask = true,
                layerMask = config.BlockingLayers.value
            };
            solidFilter = new ContactFilter2D
            {
                useTriggers = false,
                useLayerMask = true,
                layerMask =
                    config.BlockingLayers.value |
                    config.GroundLayers.value
            };
        }

        public bool HasSupport(Vector2 position, float angle)
        {
            return TryGetSupportHit(
                position,
                angle,
                config.GroundProbeDistance + config.CollisionSkin,
                out _);
        }

        public bool Step(float targetX, float deltaTime)
        {
            float safeDeltaTime = Mathf.Max(0.0001f, deltaTime);
            float deltaX = targetX - state.Position.x;
            bool horizontallyBlocked =
                IsMovementBlocked(deltaX);
            if (UpdateBlockedState(
                    horizontallyBlocked,
                    deltaX,
                    safeDeltaTime))
            {
                return true;
            }

            Vector2 nextPosition = state.Position;
            if (!horizontallyBlocked)
                nextPosition.x = targetX;

            if (state.Grounded &&
                !HasSupport(
                    new Vector2(nextPosition.x, state.Position.y),
                    state.Angle))
            {
                state.Grounded = false;
                state.VerticalSpeed = 0f;
            }

            if (state.Grounded)
            {
                state.VerticalSpeed = 0f;
                state.FastFallActive = false;
                state.Angle = config.UprightAngle;
            }
            else
            {
                state.Angle = Mathf.DeltaAngle(
                    0f,
                    state.Angle +
                    config.AngularSpeed * safeDeltaTime);
                float multiplier =
                    state.FastFallActive
                        ? config.FastFallGravityMultiplier
                        : 1f;
                state.VerticalSpeed +=
                    GravityAccelerationY *
                    multiplier *
                    safeDeltaTime;

                ResolveVerticalMotion(
                    ref nextPosition,
                    ref state.Angle,
                    state.VerticalSpeed * safeDeltaTime);
            }

            state.Position = nextPosition;
            return false;
        }

        private bool UpdateBlockedState(
            bool horizontallyBlocked,
            float deltaX,
            float deltaTime)
        {
            if (Mathf.Abs(deltaX) < config.MinMoveDistance)
            {
                ResetBlockedState();
                return false;
            }

            int direction = deltaX > 0f ? 1 : -1;
            if (state.BlockedDirection != direction)
            {
                ResetBlockedState();
                state.BlockedDirection = direction;
            }

            if (!horizontallyBlocked)
            {
                state.BlockedElapsed = 0f;
                return false;
            }

            state.BlockedElapsed += deltaTime;
            if (state.BlockedElapsed < config.BlockedCheckWindow)
                return false;

            ResetBlockedState();
            return true;
        }

        private void ResetBlockedState()
        {
            state.BlockedElapsed = 0f;
            state.BlockedDirection = 0;
        }

        private bool IsMovementBlocked(float deltaX)
        {
            float distance = Mathf.Abs(deltaX);
            if (distance < config.MinMoveDistance)
                return false;

            Vector2 direction =
                deltaX > 0f ? Vector2.right : Vector2.left;
            Vector2 castPosition = state.Position;
            if (state.Grounded)
            {
                castPosition +=
                    Vector2.up *
                    SupportNormalY *
                    (config.GroundProbeDistance +
                     config.CollisionSkin +
                     0.02f);
            }

            int count = CastAtPose(
                castPosition,
                state.Angle,
                direction,
                distance + config.CollisionSkin,
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

        private bool TryGetSupportHit(
            Vector2 position,
            float angle,
            float distance,
            out RaycastHit2D supportHit)
        {
            supportHit = default;
            Vector2 supportNormal =
                Vector2.up * SupportNormalY;
            float probeLift = Mathf.Max(
                config.GroundProbeDistance,
                config.CollisionSkin * 2f);
            Vector2 probePosition =
                position + supportNormal * probeLift;
            int count = CastAtPose(
                probePosition,
                angle,
                -supportNormal,
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

            Vector2 direction =
                displacement > 0f ? Vector2.up : Vector2.down;
            int count = CastAtPose(
                position,
                angle,
                direction,
                Mathf.Abs(displacement) + config.CollisionSkin,
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
                Mathf.Max(
                    0f,
                    nearestDistance - config.CollisionSkin);
            position += direction * allowedDistance;

            bool movingTowardSupport =
                direction.y * SupportNormalY < 0f;
            bool hitSupport =
                Vector2.Dot(
                    nearestHit.normal,
                    Vector2.up * SupportNormalY) > 0.5f;

            if (movingTowardSupport && hitSupport)
            {
                state.Grounded = true;
                state.FastFallActive = false;
                state.VerticalSpeed = 0f;
                LandOnSupport(
                    ref position,
                    ref angle,
                    nearestHit);
                return;
            }

            state.VerticalSpeed = 0f;
        }

        private void LandOnSupport(
            ref Vector2 position,
            ref float angle,
            RaycastHit2D hit)
        {
            angle = config.UprightAngle;

            Vector2 halfSize = GetColliderWorldSize() * 0.5f;
            float radians = angle * Mathf.Deg2Rad;
            Vector2 localX = new Vector2(
                Mathf.Cos(radians),
                Mathf.Sin(radians));
            Vector2 localY = new Vector2(
                -Mathf.Sin(radians),
                Mathf.Cos(radians));
            float supportExtent =
                Mathf.Abs(Vector2.Dot(hit.normal, localX)) *
                halfSize.x +
                Mathf.Abs(Vector2.Dot(hit.normal, localY)) *
                halfSize.y;
            Vector2 colliderOffset =
                GetColliderCenter(position, angle) - position;
            Vector2 uprightCenter =
                hit.point +
                hit.normal *
                (supportExtent + config.CollisionSkin);
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
                (body != null &&
                 hitCollider.attachedRigidbody == body))
            {
                return true;
            }

            return hitCollider.GetComponentInParent<NodeMarker>() != null;
        }

        private float GravityAccelerationY =>
            Physics2D.gravity.y *
            config.GravityScale *
            (state.GravityInverted ? -1f : 1f);

        private float SupportNormalY =>
            state.GravityInverted ? -1f : 1f;
    }
}
