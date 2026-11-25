using GreatVoyage.Ocean;
using UnityEditor;

namespace GreatVoyage.Editor
{
    [CustomEditor(typeof(WaterInteractor))]
    public sealed class WaterInteractorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var mode = serializedObject.FindProperty("mode");
            EditorGUILayout.PropertyField(mode);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("response"));

            if (mode.enumValueIndex == (int)WaterInteractor.InteractionMode.Wake)
            {
                EditorGUILayout.HelpBox("Moves with this object. An optional anchor sets the wake origin. The water surface reads this component automatically; Wake needs no Collider.", MessageType.Info);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("wakeAnchor"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("wakeWidth"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("wakeRearOffset"));
            }
            else
            {
                var shape = serializedObject.FindProperty("contactShape");
                EditorGUILayout.PropertyField(shape);
                if (!shape.objectReferenceValue)
                    EditorGUILayout.HelpBox("Assign a Box, Sphere, or Capsule Collider to detect entry and depth changes.", MessageType.Warning);
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}
