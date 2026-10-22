using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ChartPlayer))]
public class ChartPlayerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        ChartPlayer p = (ChartPlayer)target;
        if (p == null) return;

        EditorGUILayout.Space(6);

        if (p.dirty)
            EditorGUILayout.HelpBox("有未同步的谱面改动。点下方按钮烘焙，或进入 Play 时自动烘焙。", MessageType.Warning);
        else
            EditorGUILayout.HelpBox("谱面已同步。", MessageType.Info);

        if (GUILayout.Button("Bake From Scene"))
            p.Bake();
    }
}