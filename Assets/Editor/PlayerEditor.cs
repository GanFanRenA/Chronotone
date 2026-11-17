using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Player))]
public sealed class PlayerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        Player player = (Player)target;
        GameSettingsSO settings = player.GameSettings;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Game Settings", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "判定、AutoPlay、回溯、物理和判定特效参数统一保存在共享 GameSettings 中。",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField(
                "Shared Settings",
                settings,
                typeof(GameSettingsSO),
                false);
        }

        if (GUILayout.Button("Edit Game Settings"))
        {
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }
    }
}
