using UnityEditor;
using UnityEngine;

public partial class ChartTimelineWindow
{
    [SerializeField] private float nodeLaneHeight = 40f;
    [SerializeField] private float vfxLaneHeight = 56f;

    private const float MinNodeLaneHeight = 24f;
    private const float MinVfxLaneHeight = 24f;
    private const float DividerGrabHalfHeight = 3f;

    private static readonly Color DividerColor =
        new Color(0.45f, 0.45f, 0.5f);
    private static readonly Color DividerActiveColor =
        new Color(1f, 0.8f, 0.3f);

    private enum LaneDivider
    {
        None = 0,
        NodeToVfx = 1,
        VfxToAnimation = 2
    }

    private LaneDivider resizingDivider;
    private float resizeStartMouseY;
    private float resizeStartNodeHeight;
    private float resizeStartVfxHeight;

    // 把窗口剩余高度在三栏间分配：Node 与视效高度来自可序列化字段，
    // Stage 动画栏仍是弹性栏，吃掉剩余高度。
    private void ResolveLaneHeights(
        float detailHeight,
        out float nodeHeight,
        out float vfxHeight,
        out float animationHeight)
    {
        float available =
            position.height - ToolbarHeight - RulerHeight -
            ScrollbarHeight - detailHeight;
        float minAnimation = detailHeight > 0f ? 0f : MinAnimationLaneHeight;
        float maxNodeVfx = Mathf.Max(
            MinNodeLaneHeight + MinVfxLaneHeight,
            available - minAnimation);

        nodeHeight = Mathf.Clamp(
            nodeLaneHeight,
            MinNodeLaneHeight,
            maxNodeVfx - MinVfxLaneHeight);
        vfxHeight = Mathf.Clamp(
            vfxLaneHeight,
            MinVfxLaneHeight,
            maxNodeVfx - nodeHeight);
        animationHeight = Mathf.Max(
            minAnimation,
            available - nodeHeight - vfxHeight);

        // 回写被 clamp 后的值，保证字段与显示一致，也便于分隔条拖拽直接读写字段。
        nodeLaneHeight = nodeHeight;
        vfxLaneHeight = vfxHeight;
    }

    private void DrawLaneDividers(Rect trackRect, Rect vfxRect)
    {
        Rect nodeVfx = DividerRect(trackRect.yMax);
        Rect vfxAnim = DividerRect(vfxRect.yMax);

        EditorGUI.DrawRect(
            new Rect(nodeVfx.x, nodeVfx.center.y - 0.5f, nodeVfx.width, 1f),
            resizingDivider == LaneDivider.NodeToVfx
                ? DividerActiveColor
                : DividerColor);
        EditorGUI.DrawRect(
            new Rect(vfxAnim.x, vfxAnim.center.y - 0.5f, vfxAnim.width, 1f),
            resizingDivider == LaneDivider.VfxToAnimation
                ? DividerActiveColor
                : DividerColor);

        EditorGUIUtility.AddCursorRect(nodeVfx, MouseCursor.ResizeVertical);
        EditorGUIUtility.AddCursorRect(vfxAnim, MouseCursor.ResizeVertical);
    }

    private bool HandleLaneDividerInput(
        Rect trackRect,
        Rect vfxRect,
        float detailHeight)
    {
        Event e = Event.current;
        Rect nodeVfx = DividerRect(trackRect.yMax);
        Rect vfxAnim = DividerRect(vfxRect.yMax);

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (nodeVfx.Contains(e.mousePosition))
            {
                BeginLaneResize(LaneDivider.NodeToVfx, e.mousePosition.y);
                e.Use();
                RequestRepaint();
                return true;
            }

            if (vfxAnim.Contains(e.mousePosition))
            {
                BeginLaneResize(LaneDivider.VfxToAnimation, e.mousePosition.y);
                e.Use();
                RequestRepaint();
                return true;
            }
        }

        if (e.type == EventType.MouseDrag &&
            e.button == 0 &&
            resizingDivider != LaneDivider.None)
        {
            UpdateLaneResize(e.mousePosition.y, detailHeight);
            e.Use();
            RequestRepaint();
            return true;
        }

        if (e.type == EventType.MouseUp &&
            e.button == 0 &&
            resizingDivider != LaneDivider.None)
        {
            resizingDivider = LaneDivider.None;
            e.Use();
            RequestRepaint();
            return true;
        }

        return false;
    }

    private Rect DividerRect(float boundaryY) =>
        new Rect(
            0f,
            boundaryY - DividerGrabHalfHeight,
            position.width,
            DividerGrabHalfHeight * 2f);

    private void BeginLaneResize(LaneDivider divider, float mouseY)
    {
        resizingDivider = divider;
        resizeStartMouseY = mouseY;
        resizeStartNodeHeight = nodeLaneHeight;
        resizeStartVfxHeight = vfxLaneHeight;
    }

    private void UpdateLaneResize(float mouseY, float detailHeight)
    {
        float delta = mouseY - resizeStartMouseY;
        float available =
            position.height - ToolbarHeight - RulerHeight -
            ScrollbarHeight - detailHeight;
        float minAnimation = detailHeight > 0f ? 0f : MinAnimationLaneHeight;

        switch (resizingDivider)
        {
            case LaneDivider.NodeToVfx:
            {
                // Node 与视效的总高保持不变，只在这两栏之间重新分配。
                float sum = resizeStartNodeHeight + resizeStartVfxHeight;
                float node = Mathf.Clamp(
                    resizeStartNodeHeight + delta,
                    MinNodeLaneHeight,
                    sum - MinVfxLaneHeight);
                nodeLaneHeight = node;
                vfxLaneHeight = sum - node;
                break;
            }

            case LaneDivider.VfxToAnimation:
            {
                // 只改视效高度，Stage 动画栏作为弹性栏自动吸收差额。
                float maxVfx = Mathf.Max(
                    MinVfxLaneHeight,
                    available - nodeLaneHeight - minAnimation);
                vfxLaneHeight = Mathf.Clamp(
                    resizeStartVfxHeight + delta,
                    MinVfxLaneHeight,
                    maxVfx);
                break;
            }
        }
    }
}
