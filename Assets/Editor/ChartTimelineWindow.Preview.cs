using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public partial class ChartTimelineWindow
{
    private static readonly Color IncomingPreviewColor =
        new Color(0.15f, 0.8f, 1f, 0.95f);
    private static readonly Color OutgoingPreviewColor =
        new Color(1f, 0.72f, 0.16f, 0.95f);
    private static readonly Color BlockedPreviewColor =
        new Color(1f, 0.22f, 0.18f, 1f);

    private PerfectRoute cachedEditorPerfectRoute;
    private Player cachedRoutePlayer;
    private PhysicsActionMode cachedRoutePhysicsMode;
    private float cachedRouteJumpVelocity;
    private float cachedRouteGravityScale;
    private float cachedRouteFastFallImpulse;
    private float cachedRouteAngularSpeed;
    private float cachedRouteUprightAngle;
    private int cachedRouteGroundLayers;
    private int cachedRouteBlockingLayers;
    private float cachedRouteBlockedCheckWindow;
    private float cachedRouteMinMoveDistance;
    private float cachedRouteCollisionSkin;
    private float cachedRouteGroundProbeDistance;
    private float cachedRouteFixedDeltaTime;
    private double cachedRouteOffset;
    private int cachedRouteMarkerCount;
    private int cachedRouteNodeSignature;
    private bool editorPerfectRouteDirty = true;

    private sealed class PreviewPath
    {
        public readonly List<Vector3> Points = new List<Vector3>();
        public bool Blocked;
        public bool Incomplete;
    }

    private void DrawTrajectoryPreview(SceneView sceneView)
    {
        if (Event.current == null ||
            Event.current.type != EventType.Repaint ||
            !previewEnabled ||
            Application.isPlaying ||
            selection.Count != 1 ||
            targetPlayer == null ||
            targetPlayer.BpmTimeline == null)
        {
            return;
        }

        NodeMarker selected = selection[0];
        if (selected == null)
            return;

        Player player = targetPlayer.ResolvePlayer();
        PhysicsActionMode physicsMode = player != null
            ? player.GetComponent<PhysicsActionMode>()
            : null;
        BoxCollider2D bodyCollider = player != null
            ? player.GetComponent<BoxCollider2D>()
            : null;
        Rigidbody2D body = player != null
            ? player.GetComponent<Rigidbody2D>()
            : null;
        if (player == null ||
            physicsMode == null ||
            bodyCollider == null)
        {
            return;
        }

        NodeMarker[] markers = GetMarkers();
        int selectedIndex = System.Array.IndexOf(markers, selected);
        if (selectedIndex < 0)
            return;

        PerfectRoute route = BuildEditorPerfectRoute(
            player,
            physicsMode,
            bodyCollider,
            body,
            markers);
        if (route == null || route.Count == 0)
            return;

        if (selectedIndex > 0)
        {
            DrawRouteSegment(
                route,
                markers[selectedIndex - 1],
                selected,
                selected.transform.position.z,
                IncomingPreviewColor,
                targetPlayer.BpmTimeline);
        }

        if (selectedIndex + 1 < markers.Length)
        {
            DrawRouteSegment(
                route,
                selected,
                markers[selectedIndex + 1],
                selected.transform.position.z,
                OutgoingPreviewColor,
                targetPlayer.BpmTimeline);
        }
    }

    private void AdjustSelectedNodesToPerfectPath()
    {
        if (targetPlayer == null ||
            targetPlayer.BpmTimeline == null ||
            selection.Count == 0)
        {
            ShowNotification(
                new GUIContent("请先选择要调整 Y 的节点"));
            return;
        }

        Player player = targetPlayer.ResolvePlayer();
        PhysicsActionMode physicsMode = player != null
            ? player.GetComponent<PhysicsActionMode>()
            : null;
        BoxCollider2D bodyCollider = player != null
            ? player.GetComponent<BoxCollider2D>()
            : null;
        Rigidbody2D body = player != null
            ? player.GetComponent<Rigidbody2D>()
            : null;
        if (player == null ||
            physicsMode == null ||
            bodyCollider == null)
        {
            ShowNotification(new GUIContent(
                "找不到带 PhysicsActionMode 和 BoxCollider2D 的 Player"));
            return;
        }

        NodeMarker[] markers = GetMarkers();
        PerfectRoute route = BuildEditorPerfectRoute(
            player,
            physicsMode,
            bodyCollider,
            body,
            markers);
        if (route == null || route.Count == 0)
        {
            ShowNotification(
                new GUIContent("无法生成有效完美路线"));
            return;
        }

        var orderedSelection = new List<NodeMarker>();
        foreach (NodeMarker marker in markers)
        {
            if (marker != null && IsSelected(marker))
                orderedSelection.Add(marker);
        }
        if (orderedSelection.Count == 0)
        {
            ShowNotification(
                new GUIContent("所选对象不包含当前谱面的节点"));
            return;
        }

        var undoObjects = new List<Object>(orderedSelection.Count * 2);
        foreach (NodeMarker marker in orderedSelection)
        {
            undoObjects.Add(marker);
            undoObjects.Add(marker.transform);
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Adjust Node Y To Perfect Path");
        Undo.RegisterCompleteObjectUndo(
            undoObjects.ToArray(),
            "Adjust Node Y To Perfect Path");

        int adjustedCount = 0;
        int skippedCount = 0;
        foreach (NodeMarker target in orderedSelection)
        {
            double targetTime =
                targetPlayer.BpmTimeline.BeatToSeconds(
                    target.beat);
            if (!route.TrySampleAt(
                    targetTime,
                    out PerfectRouteSample sample))
            {
                skippedCount++;
                continue;
            }

            Vector3 position = target.transform.position;
            if (!Mathf.Approximately(position.y, sample.Position.y))
            {
                position.y = sample.Position.y;
                target.transform.position = position;
                EditorUtility.SetDirty(target);
                EditorUtility.SetDirty(target.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(
                    target.transform);
            }
            adjustedCount++;
        }

        Undo.CollapseUndoOperations(undoGroup);
        if (adjustedCount > 0)
        {
            targetPlayer.MarkDirty();
            InvalidateMarkers();
            SceneView.RepaintAll();
        }

        ShowNotification(new GUIContent(
            skippedCount > 0
                ? $"已按从前到后调整 {adjustedCount} 个节点，跳过 {skippedCount} 个"
                : $"已按从前到后调整 {adjustedCount} 个节点"));
    }

    private PerfectRoute BuildEditorPerfectRoute(
        Player player,
        PhysicsActionMode physicsMode,
        BoxCollider2D bodyCollider,
        Rigidbody2D body,
        NodeMarker[] markers)
    {
        if (targetPlayer == null ||
            targetPlayer.BpmTimeline == null ||
            markers == null ||
            markers.Length == 0)
        {
            return null;
        }

        double chartOffset =
            targetPlayer.GlobalOffsetSeconds;
        float uprightAngle =
            player.transform.localEulerAngles.z;
        int nodeSignature = ComputeRouteNodeSignature(markers);
        if (!editorPerfectRouteDirty &&
            cachedEditorPerfectRoute != null &&
            cachedRoutePlayer == player &&
            cachedRoutePhysicsMode == physicsMode &&
            cachedRouteMarkerCount == markers.Length &&
            cachedRouteNodeSignature == nodeSignature &&
            Mathf.Approximately(
                cachedRouteJumpVelocity,
                physicsMode.JumpVelocity) &&
            Mathf.Approximately(
                cachedRouteGravityScale,
                physicsMode.GravityScale) &&
            Mathf.Approximately(
                cachedRouteFastFallImpulse,
                physicsMode.FastFallImpulse) &&
            Mathf.Approximately(
                cachedRouteAngularSpeed,
                physicsMode.AngularSpeed) &&
            Mathf.Approximately(
                cachedRouteUprightAngle,
                uprightAngle) &&
            cachedRouteGroundLayers ==
            physicsMode.GroundLayers.value &&
            cachedRouteBlockingLayers ==
            physicsMode.BlockingLayers.value &&
            Mathf.Approximately(
                cachedRouteBlockedCheckWindow,
                physicsMode.BlockedCheckWindow) &&
            Mathf.Approximately(
                cachedRouteMinMoveDistance,
                physicsMode.MinMoveDistance) &&
            Mathf.Approximately(
                cachedRouteCollisionSkin,
                physicsMode.CollisionSkin) &&
            Mathf.Approximately(
                cachedRouteGroundProbeDistance,
                physicsMode.GroundProbeDistance) &&
            Mathf.Approximately(
                cachedRouteFixedDeltaTime,
                Time.fixedDeltaTime) &&
            System.Math.Abs(cachedRouteOffset - chartOffset) <=
            0.000001d)
        {
            return cachedEditorPerfectRoute;
        }

        var nodes = new List<NodeData>(markers.Length);
        double endSongTime = -chartOffset;
        foreach (NodeMarker marker in markers)
        {
            if (marker == null)
                continue;

            var node = new NodeData
            {
                beat = marker.beat,
                order = marker.order,
                action = marker.action,
                gravity = marker.gravity,
                auto = marker.auto,
                visible = marker.visible,
                strength = marker.strength,
                position = marker.transform.position,
                deltaX = marker.deltaX
            };
            node.MarkMigrated();
            nodes.Add(node);
            endSongTime = System.Math.Max(
                endSongTime,
                targetPlayer.BpmTimeline.BeatToSeconds(
                    marker.beat));
        }

        if (nodes.Count == 0)
            return null;

        endSongTime += 1d;
        var config = new PerfectRouteConfig(physicsMode, uprightAngle);
        StageCollisionPresence presence = StageCollisionPresence.Build(
            targetPlayer.BpmTimeline,
            targetPlayer.gameObject.scene.GetRootGameObjects());
        cachedEditorPerfectRoute = PerfectRouteBuilder.Build(
            player.transform.position,
            -chartOffset,
            nodes,
            targetPlayer.BpmTimeline,
            config,
            bodyCollider,
            body,
            endSongTime,
            presence.IsActive);
        cachedRoutePlayer = player;
        cachedRoutePhysicsMode = physicsMode;
        cachedRouteJumpVelocity = physicsMode.JumpVelocity;
        cachedRouteGravityScale = physicsMode.GravityScale;
        cachedRouteFastFallImpulse =
            physicsMode.FastFallImpulse;
        cachedRouteAngularSpeed = physicsMode.AngularSpeed;
        cachedRouteUprightAngle = uprightAngle;
        cachedRouteGroundLayers = physicsMode.GroundLayers.value;
        cachedRouteBlockingLayers = physicsMode.BlockingLayers.value;
        cachedRouteBlockedCheckWindow =
            physicsMode.BlockedCheckWindow;
        cachedRouteMinMoveDistance = physicsMode.MinMoveDistance;
        cachedRouteCollisionSkin = physicsMode.CollisionSkin;
        cachedRouteGroundProbeDistance =
            physicsMode.GroundProbeDistance;
        cachedRouteFixedDeltaTime = Time.fixedDeltaTime;
        cachedRouteOffset = chartOffset;
        cachedRouteMarkerCount = markers.Length;
        cachedRouteNodeSignature = nodeSignature;
        editorPerfectRouteDirty = false;
        return cachedEditorPerfectRoute;
    }

    /// <summary>
    /// 影响完美轨道的节点字段签名。力度、动作、重力、拍点和位置变化时
    /// 即使没有 Undo 事件也会让预览路线重建。
    /// </summary>
    private static int ComputeRouteNodeSignature(NodeMarker[] markers)
    {
        unchecked
        {
            int hash = 17;
            foreach (NodeMarker marker in markers)
            {
                if (marker == null)
                    continue;

                hash = hash * 31 + marker.beat.GetHashCode();
                hash = hash * 31 + marker.order;
                hash = hash * 31 + (int)marker.action;
                hash = hash * 31 + (int)marker.gravity;
                hash = hash * 31 + marker.strength.GetHashCode();
                Vector3 position = marker.transform.position;
                hash = hash * 31 + position.x.GetHashCode();
                hash = hash * 31 + position.y.GetHashCode();
            }

            return hash;
        }
    }

    private static void DrawRouteSegment(
        PerfectRoute route,
        NodeMarker from,
        NodeMarker to,
        float z,
        Color color,
        BpmTimeline bpmTimeline)
    {
        double fromTime = bpmTimeline.BeatToSeconds(from.beat);
        double toTime = bpmTimeline.BeatToSeconds(to.beat);
        if (toTime < fromTime)
        {
            double swap = fromTime;
            fromTime = toTime;
            toTime = swap;
        }

        var path = new PreviewPath
        {
            Blocked = route.Blocked,
            Incomplete = !route.IsComplete
        };
        if (route.TrySampleAt(fromTime, out PerfectRouteSample first))
            path.Points.Add(new Vector3(first.Position.x, first.Position.y, z));

        foreach (PerfectRouteSample sample in route.Samples)
        {
            if (sample.Time <= fromTime || sample.Time >= toTime)
                continue;

            path.Points.Add(new Vector3(
                sample.Position.x,
                sample.Position.y,
                z));
        }

        if (route.TrySampleAt(toTime, out PerfectRouteSample last))
            path.Points.Add(new Vector3(last.Position.x, last.Position.y, z));

        DrawPreviewPath(path, color, BlockedPreviewColor, 4f);
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
}
