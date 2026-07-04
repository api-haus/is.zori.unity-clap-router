using UnityEditor;
using UnityEngine;

namespace Zori.ClapRouter.Editor
{
    [CustomEditor(typeof(ClapDeviceDefinition))]
    public sealed class ClapDeviceDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("role"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("pluginIndex"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Per-platform CLAP binary", EditorStyles.boldLabel);

            SerializedProperty binaries = serializedObject.FindProperty("binaries");
            for (int i = 0; i < binaries.arraySize; i++)
            {
                SerializedProperty element = binaries.GetArrayElementAtIndex(i);
                SerializedProperty platform = element.FindPropertyRelative("platform");
                SerializedProperty path = element.FindPropertyRelative("path");

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(platform, GUIContent.none, GUILayout.Width(90));
                EditorGUILayout.PropertyField(path, GUIContent.none);
                if (GUILayout.Button("Browse…", GUILayout.Width(70)))
                {
                    string title =
                        "Select .clap for " + (ClapTargetPlatform)platform.enumValueIndex;
                    string start = string.IsNullOrEmpty(path.stringValue)
                        ? Application.dataPath
                        : System.IO.Path.GetDirectoryName(path.stringValue);
                    string picked = EditorUtility.OpenFilePanel(title, start, "clap");
                    if (!string.IsNullOrEmpty(picked))
                    {
                        path.stringValue = picked;
                    }
                }
                if (GUILayout.Button("−", GUILayout.Width(24)))
                {
                    binaries.DeleteArrayElementAtIndex(i);
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Add platform"))
            {
                binaries.arraySize++;
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            ClapDeviceDefinition device = (ClapDeviceDefinition)target;
            bool ok = device.ExistsForCurrentPlatform();
            EditorGUILayout.HelpBox(
                ok
                    ? $"Resolves on this editor's platform ({ClapDeviceDefinition.CurrentPlatform})."
                    : $"No existing binary for this editor's platform ({ClapDeviceDefinition.CurrentPlatform}). "
                        + "Add its row and Browse to the built .clap.",
                ok ? MessageType.Info : MessageType.Warning
            );
        }
    }
}
