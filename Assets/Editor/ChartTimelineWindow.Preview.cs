using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public partial class ChartTimelineWindow
{
    private const int MaxPreviewSimulationSteps = 8192;

    private static readonly Color IncomingPreviewColor =
        new Color(0.15f, 0.8f, 1f, 0.95f);
    private static readonly Color OutgoingPreviewColor =
        new Color(1f, 0.72f, 0.16f, 0.95f);
    private static readonly Color BlockedPreviewColor =
        new Color(1f, 0.22f, 0.18f, 1f);
    private static readonly Color EarliestPreviewColor =
        new Color(0.35f, 1f, 0.55f, 0.9f);
    private static readonly Color LatestPreviewColor =
        new Color(1f, 0.45f, 0.9f, 0.9f);

    private sealed class PreviewPath
    {
        public readonly List<Vector3> Points = new List<Vector3>();
        public bool Blocked;
        public bool Incomplete;
    }

    private sealed class PreviewXTimeline
    {
        private readonly float[] times;
        private readonly float[] positions;

        public PreviewXTimeline(
            NodeMarker[] markers,
            BpmTimeline bpmTimeline)
        {
            times = new float[markers.Length];
            positions = new float[markers.Length];

            for (int i = 0; i < markers.Length; i++)
            {
                times[i] = bpmTimeline.BeatToSeconds(markers[i].beat);
                positions[i] = markers[i].transform.position.x;
            }
        }

        public float SampleX(float songTime)
        {
            if (times.Length == 0)
                return 0f;
            if (songTime < times[0])
                return positions[0];
            if (songTime >= times[times.Length - 1])
                return positions[positions.Length - 1];

            int low = 0;
            int high = times.Length - 1;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (times[middle] <= songTime)
                    low = middle + 1;
                else
                    high = middle;
            }

            int upperIndex = low;
            int lowerIndex = upperIndex - 1;
            float span =
                times[upperIndex] - times[lowerIndex];
            if (span <= 0.000001f)
                return positions[upperIndex];

            float progress = Mathf.Clamp01(
                (songTime - times[lowerIndex]) / span);
            return Mathf.Lerp(
                positions[lowerIndex],
                positions[upperIndex],
                progress);
        }
    }

    private readonly struct PreviewPhysicsConfig
    {
        public PreviewPhysicsConfig(PhysicsActionMode mode, Player player)
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
            UprightAngle = player.transform.localEulerAngles.z;
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

    private sealed class PreviewPhysicsState
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

    private sealed class PreviewPhysicsSimulation
    {
        private readonly PreviewPhysicsConfig config;
        private readonly BoxCollider2D bodyCollider;
        private readonly Rigidbody2D body;
        private readonly ContactFilter2D groundFilter;
        private readonly ContactFilter2D blockingFilter;
        private readonly ContactFilter2D solidFilter;
        private readonly RaycastHit2D[] castHits = new RaycastHit2D[16];
        private readonly PreviewPhysicsState state;

        public PreviewPhysicsSimulation(
            PreviewPhysicsConfig config,
            BoxCollider2D bodyCollider,
            Rigidbody2D body,
            PreviewPhysicsState state)
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

        public Vector2 Position => state.Position;

        public void StartFastFall()
        {
            state.Grounded = false;
            state.VerticalSpeed = 0f;
            state.FastFallActive = true;
        }

        public bool Step(float targetX, float deltaTime)
        {
            float safeDeltaTime = Mathf.Max(0.0001f, deltaTime);
            float deltaX = targetX - state.Position.x;
            bool horizontallyBlocked = IsMovementBlocked(deltaX);
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
                state.Angle = NormalizeAngle(config.UprightAngle);
            }
            else
            {
                state.Angle = NormalizeAngle(
                    state.Angle +
                    config.AngularSpeed * safeDeltaTime);
                state.VerticalSpeed +=
                    GravityAccelerationY *
                    (state.FastFallActive
                        ? config.FastFallGravityMultiplier
                        : 1f) *
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

            Vector2 direction = deltaX > 0f
                ? Vector2.right
                : Vector2.left;
            Vector2 castPosition = state.Position;
            if (state.Grounded)
            {
                castPosition += Vector2.up * SupportNormalY *
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

        private bool HasSupport(Vector2 position, float angle)
        {
            return TryGetSupportHit(
                position,
                angle,
                config.GroundProbeDistance + config.CollisionSkin,
                out _);
        }

        private bool TryGetSupportHit(
            Vector2 position,
            float angle,
            float distance,
            out RaycastHit2D supportHit)
        {
            supportHit = default;

            Vector2 supportNormal = Vector2.up * SupportNormalY;
            Vector2 castDirection = -supportNormal;
            float probeLift = Mathf.Max(
                config.GroundProbeDistance,
                config.CollisionSkin * 2f);
            Vector2 probePosition =
                position + supportNormal * probeLift;
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
                {
                    continue;
                }

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
                Mathf.Max(0f, nearestDistance - config.CollisionSkin);
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
                LandOnSupport(ref position, ref angle, nearestHit);
                return;
            }

            state.VerticalSpeed = 0f;
        }

        private void LandOnSupport(
            ref Vector2 position,
            ref float angle,
            RaycastHit2D hit)
        {
            angle = NormalizeAngle(config.UprightAngle);

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
                hit.point +
                hit.normal * (supportExtent + config.CollisionSkin);
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
                (body != null && hitCollider.attachedRigidbody == body))
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

        private static float NormalizeAngle(float angle)
        {
            return Mathf.DeltaAngle(0f, angle);
        }
    }

    private void DrawTrajectoryPreview(SceneView sceneView)
    {
        if (Event.current == null ||
            Event.current.type != EventType.Repaint ||
            !previewEnabled ||
            Application.isPlaying ||
            selection.Count != 1 ||
            targetPlayer == null ||
            targetPlayer.chart == null ||
            targetPlayer.chart.bpmTimeline == null)
        {
            return;
        }

        NodeMarker selected = selection[0];
        if (selected == null)
            return;

        Transform root = targetPlayer.scanRoot != null
            ? targetPlayer.scanRoot
            : targetPlayer.transform;
        if (!selected.transform.IsChildOf(root))
            return;

        Player player = ResolvePreviewPlayer(targetPlayer);
        if (player == null)
            return;

        BasicJudgement judgement =
            player.GetComponent<BasicJudgement>();
        PhysicsActionMode physicsMode =
            player.GetComponent<PhysicsActionMode>();
        BoxCollider2D bodyCollider =
            player.GetComponent<BoxCollider2D>();
        if (physicsMode == null || bodyCollider == null)
            return;

        NodeMarker[] markers =
            root.GetComponentsInChildren<NodeMarker>(false);
        System.Array.Sort(markers, (a, b) =>
        {
            int byBeat = a.beat.CompareTo(b.beat);
            return byBeat != 0
                ? byBeat
                : a.order.CompareTo(b.order);
        });

        int selectedIndex =
            System.Array.IndexOf(markers, selected);
        if (selectedIndex < 0)
            return;

        var xTimeline = new PreviewXTimeline(
            markers,
            targetPlayer.chart.bpmTimeline);
        PreviewPhysicsConfig config =
            new PreviewPhysicsConfig(physicsMode, player);
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        float operationHalfWindow = judgement != null
            ? Mathf.Max(0f, judgement.GreatWindowSeconds)
            : 0f;
        Physics2D.SyncTransforms();

        if (selectedIndex > 0)
        {
            DrawSegmentPreviewVariants(
                markers,
                selectedIndex - 1,
                selectedIndex,
                targetPlayer,
                config,
                bodyCollider,
                body,
                xTimeline,
                operationHalfWindow,
                IncomingPreviewColor);
        }

        if (selectedIndex + 1 < markers.Length)
        {
            DrawSegmentPreviewVariants(
                markers,
                selectedIndex,
                selectedIndex + 1,
                targetPlayer,
                config,
                bodyCollider,
                body,
                xTimeline,
                operationHalfWindow,
                OutgoingPreviewColor);
        }
    }

    private static Player ResolvePreviewPlayer(
        ChartPlayer chartPlayer)
    {
        Player[] players = Object.FindObjectsByType<Player>(
            FindObjectsSortMode.None);
        foreach (Player player in players)
        {
            if (player != null && player.chartPlayer == chartPlayer)
                return player;
        }

        return players.Length == 1 ? players[0] : null;
    }

    private void DrawSegmentPreviewVariants(
        NodeMarker[] markers,
        int sourceIndex,
        int targetIndex,
        ChartPlayer chartPlayer,
        PreviewPhysicsConfig config,
        BoxCollider2D bodyCollider,
        Rigidbody2D body,
        PreviewXTimeline xTimeline,
        float operationHalfWindow,
        Color exactColor)
    {
        if (operationHalfWindow > 0.00001f)
        {
            PreviewPath earliest = SimulatePreviewSegment(
                markers,
                sourceIndex,
                targetIndex,
                chartPlayer,
                config,
                bodyCollider,
                body,
                xTimeline,
                -operationHalfWindow);
            DrawPreviewPath(
                earliest,
                EarliestPreviewColor,
                BlockedPreviewColor,
                2.5f);

            PreviewPath latest = SimulatePreviewSegment(
                markers,
                sourceIndex,
                targetIndex,
                chartPlayer,
                config,
                bodyCollider,
                body,
                xTimeline,
                operationHalfWindow);
            DrawPreviewPath(
                latest,
                LatestPreviewColor,
                BlockedPreviewColor,
                2.5f);
        }

        PreviewPath exact = SimulatePreviewSegment(
            markers,
            sourceIndex,
            targetIndex,
            chartPlayer,
            config,
            bodyCollider,
            body,
            xTimeline,
            0f);
        DrawPreviewPath(
            exact,
            exactColor,
            BlockedPreviewColor,
            4f);
    }

    private PreviewPath SimulatePreviewSegment(
        NodeMarker[] markers,
        int sourceIndex,
        int targetIndex,
        ChartPlayer chartPlayer,
        PreviewPhysicsConfig config,
        BoxCollider2D bodyCollider,
        Rigidbody2D body,
        PreviewXTimeline xTimeline,
        float operationOffsetSeconds)
    {
        NodeMarker source = markers[sourceIndex];
        NodeMarker target = markers[targetIndex];
        var path = new PreviewPath();

        float sourceTime = chartPlayer.chart.bpmTimeline
            .BeatToSeconds(source.beat);
        float targetTime = chartPlayer.chart.bpmTimeline
            .BeatToSeconds(target.beat);
        float operationTime = sourceTime + operationOffsetSeconds;
        float startX = xTimeline.SampleX(operationTime);
        Vector3 start = new Vector3(
            startX,
            source.transform.position.y,
            source.transform.position.z);
        Vector2 startPosition = start;
        bool gravityInverted =
            IsGravityInvertedAtNode(markers, sourceIndex);
        float supportNormalY = gravityInverted ? -1f : 1f;

        var state = new PreviewPhysicsState
        {
            Position = startPosition,
            Angle = NormalizeAngle(config.UprightAngle),
            Grounded = false,
            GravityInverted = gravityInverted,
            VerticalSpeed =
                source.mode == NodeMode.JumpNode ||
                source.mode == NodeMode.GravityJumpNode
                ? supportNormalY * config.JumpVelocity
                : 0f
        };

        path.Points.Add(start);

        float duration = Mathf.Max(0f, targetTime - operationTime);
        if (duration <= 0.00001f)
        {
            path.Points.Add(target.transform.position);
            return path;
        }

        var simulation = new PreviewPhysicsSimulation(
            config,
            bodyCollider,
            body,
            state);
        if (source.mode == NodeMode.GravityNode ||
            source.mode == NodeMode.FastFallNode ||
            source.mode == NodeMode.GravityFastFallNode)
        {
            simulation.StartFastFall();
        }

        float fixedStep = Mathf.Max(0.0001f, Time.fixedDeltaTime);
        float elapsed = 0f;
        int stepCount = 0;

        while (elapsed < duration - 0.000001f)
        {
            if (stepCount >= MaxPreviewSimulationSteps)
            {
                path.Incomplete = true;
                break;
            }

            float deltaTime = Mathf.Min(
                fixedStep,
                duration - elapsed);
            elapsed += deltaTime;
            stepCount++;

            float targetX = xTimeline.SampleX(
                operationTime + elapsed);

            if (simulation.Step(targetX, deltaTime))
            {
                path.Blocked = true;
                break;
            }

            path.Points.Add(new Vector3(
                simulation.Position.x,
                simulation.Position.y,
                start.z));
        }

        return path;
    }

    private static bool IsGravityInvertedAtNode(
        NodeMarker[] markers,
        int sourceIndex)
    {
        bool inverted = false;
        int lastIndex = Mathf.Min(sourceIndex, markers.Length - 1);
        for (int i = 0; i <= lastIndex; i++)
        {
            if (markers[i].mode == NodeMode.GravityNode ||
                markers[i].mode == NodeMode.GravityFastFallNode ||
                markers[i].mode == NodeMode.WeakGravityNode ||
                markers[i].mode == NodeMode.GravityJumpNode)
                inverted = !inverted;
        }

        return inverted;
    }

    private static void DrawPreviewPath(
        PreviewPath path,
        Color regularColor,
        Color blockColor,
        float width)
    {
        if (path == null || path.Points.Count == 0)
            return;

        CompareFunction previousZTest = Handles.zTest;
        Color previousColor = Handles.color;
        Handles.zTest = CompareFunction.Always;
        Handles.color = path.Blocked ? blockColor : regularColor;
        if (path.Points.Count >= 2)
            Handles.DrawAAPolyLine(width, path.Points.ToArray());

        Vector3 end = path.Points[path.Points.Count - 1];
        float radius =
            HandleUtility.GetHandleSize(end) *
            (path.Blocked ? 0.075f : 0.045f);
        Handles.DrawSolidDisc(
            end,
            Vector3.forward,
            radius);

        if (path.Incomplete)
        {
            Handles.color = BlockedPreviewColor;
            Handles.DrawWireDisc(
                end,
                Vector3.forward,
                radius * 1.8f);
        }

        Handles.color = previousColor;
        Handles.zTest = previousZTest;
    }

    private static float NormalizeAngle(float angle)
    {
        return Mathf.DeltaAngle(0f, angle);
    }
}
