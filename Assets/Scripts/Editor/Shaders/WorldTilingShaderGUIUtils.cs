using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarGame.Editor.Shaders
{
    public struct TilingProperties
    {
        public MaterialProperty tilingOn;
        public MaterialProperty tileSize;
        public MaterialProperty tileOffset;
        public MaterialProperty blendShift;
        public MaterialProperty space;
        public MaterialProperty psxSnap;
        public MaterialProperty psxSnapResolution;
        public MaterialProperty psxSnapStartDistance;
        public MaterialProperty psxSnapFullDistance;
        public MaterialProperty psxAffine;

        public TilingProperties(MaterialProperty[] properties)
        {
            tilingOn = BaseShaderGUI.FindProperty("_TilingOn", properties, false);
            tileSize = BaseShaderGUI.FindProperty("_TileSize", properties, false);
            tileOffset = BaseShaderGUI.FindProperty("_TileOffset", properties, false);
            blendShift = BaseShaderGUI.FindProperty("_BlendShift", properties, false);
            space = BaseShaderGUI.FindProperty("_SPACE", properties, false);
            psxSnap = BaseShaderGUI.FindProperty("_PSXSnap", properties, false);
            psxSnapResolution = BaseShaderGUI.FindProperty("_PSXSnapResolution", properties, false);
            psxSnapStartDistance = BaseShaderGUI.FindProperty("_PSXSnapStartDistance", properties, false);
            psxSnapFullDistance = BaseShaderGUI.FindProperty("_PSXSnapFullDistance", properties, false);
            psxAffine = BaseShaderGUI.FindProperty("_PSXAffine", properties, false);
        }
    }

    public static class WorldTilingShaderGUIUtils
    {
        private static readonly GUIContent tilingHeader = EditorGUIUtility.TrTextContent("Tiling by Model");
        private static readonly GUIContent tilingOnText = EditorGUIUtility.TrTextContent("Tiling by Model",
            "Трипланарная проекция Base Map вместо UV меша");
        private static readonly GUIContent tileSizeText = EditorGUIUtility.TrTextContent("Tile Size",
            "Сколько мировых единиц занимает один повтор текстуры");
        private static readonly GUIContent tileOffsetText = EditorGUIUtility.TrTextContent("Tile Offset");
        private static readonly GUIContent blendShiftText = EditorGUIUtility.TrTextContent("Triplanar Blend Sharpness",
            "Резкость перехода между проекциями");
        private static readonly GUIContent spaceText = EditorGUIUtility.TrTextContent("UV Space",
            "World — текстура привязана к сцене. Object — текстура закреплена на позиции модели на старте сцены и не двигается вместе с ней");
        private static readonly GUIContent psxSnapText = EditorGUIUtility.TrTextContent("PSX Vertex Snap",
            "Дрожание полигонов в экранной сетке (эффект PS1/Ultrakill). Ближе Snap Start Distance искажений нет");
        private static readonly GUIContent snapResolutionText = EditorGUIUtility.TrTextContent("Snap Resolution",
            "Разрешение сетки вершин: меньше значение — грубее дрожание полигонов (240 — как на PlayStation 1)");
        private static readonly GUIContent snapStartText = EditorGUIUtility.TrTextContent("Snap Start Distance",
            "Ближе этого расстояния (в мировых единицах) модель отображается без искажений");
        private static readonly GUIContent snapFullText = EditorGUIUtility.TrTextContent("Snap Full Distance",
            "На этом расстоянии искажение достигает максимума");
        private static readonly GUIContent psxAffineText = EditorGUIUtility.TrTextContent("PSX Affine Mapping",
            "Текстура искажается вместе с полигоном без перспективной коррекции — характерный вид PS1");

        private static readonly GUIContent[] spaceLabels =
        {
            new GUIContent("World"),
            new GUIContent("Object (frozen)")
        };

        public static void DrawTilingArea(MaterialEditor materialEditor, TilingProperties tiling)
        {
            if (tiling.tilingOn == null)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(tilingHeader, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUI.BeginChangeCheck();
            bool tilingEnabled = EditorGUILayout.Toggle(tilingOnText, tiling.tilingOn.floatValue > 0.5f);
            if (EditorGUI.EndChangeCheck())
                SetToggle(materialEditor, tiling.tilingOn, tilingEnabled, "_TILING_ON");

            bool tilingActive = tilingEnabled;

            using (new EditorGUI.DisabledScope(!tilingActive))
            {
                if (tiling.tileSize != null)
                    materialEditor.ShaderProperty(tiling.tileSize, tileSizeText);
                if (tiling.tileOffset != null)
                    materialEditor.VectorProperty(tiling.tileOffset, tileOffsetText);
                if (tiling.blendShift != null)
                    materialEditor.ShaderProperty(tiling.blendShift, blendShiftText);
                if (tiling.space != null)
                {
                    EditorGUI.BeginChangeCheck();
                    int currentIndex = Mathf.Clamp(Mathf.RoundToInt(tiling.space.floatValue), 0, spaceLabels.Length - 1);
                    int newIndex = EditorGUILayout.Popup(spaceText, currentIndex, spaceLabels);
                    if (EditorGUI.EndChangeCheck())
                        SetSpace(materialEditor, tiling.space, newIndex);
                }
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space();

            if (tiling.psxSnap != null)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                bool snapEnabled = EditorGUILayout.Toggle(psxSnapText, tiling.psxSnap.floatValue > 0.5f);
                if (EditorGUI.EndChangeCheck())
                    SetToggle(materialEditor, tiling.psxSnap, snapEnabled, "_PSX_SNAP_ON");

                using (new EditorGUI.DisabledScope(!snapEnabled))
                {
                    if (tiling.psxSnapResolution != null)
                        materialEditor.ShaderProperty(tiling.psxSnapResolution, snapResolutionText);
                    if (tiling.psxSnapStartDistance != null)
                        materialEditor.FloatProperty(tiling.psxSnapStartDistance, snapStartText.text);
                    if (tiling.psxSnapFullDistance != null)
                        materialEditor.FloatProperty(tiling.psxSnapFullDistance, snapFullText.text);
                }

                if (tiling.psxAffine != null)
                {
                    EditorGUI.BeginChangeCheck();
                    bool affineEnabled = EditorGUILayout.Toggle(psxAffineText, tiling.psxAffine.floatValue > 0.5f);
                    if (EditorGUI.EndChangeCheck())
                        SetToggle(materialEditor, tiling.psxAffine, affineEnabled, "_PSX_AFFINE");
                }

                EditorGUI.indentLevel--;
            }
        }

        public static void ApplyKeywords(Material material)
        {
            if (material.HasProperty("_TilingOn"))
                CoreUtils.SetKeyword(material, "_TILING_ON", material.GetFloat("_TilingOn") > 0.5f);

            if (material.HasProperty("_PSXSnap"))
                CoreUtils.SetKeyword(material, "_PSX_SNAP_ON", material.GetFloat("_PSXSnap") > 0.5f);

            if (material.HasProperty("_PSXAffine"))
                CoreUtils.SetKeyword(material, "_PSX_AFFINE", material.GetFloat("_PSXAffine") > 0.5f);

            if (material.HasProperty("_SPACE"))
                ApplySpaceKeyword(material, Mathf.RoundToInt(material.GetFloat("_SPACE")));
        }

        private static void ApplySpaceKeyword(Material material, int value)
        {
            bool worldSpace = value == 0;
            CoreUtils.SetKeyword(material, "_SPACE_WORLD", worldSpace);
            CoreUtils.SetKeyword(material, "_SPACE_OBJECT", !worldSpace);
        }

        private static void SetToggle(MaterialEditor materialEditor, MaterialProperty property, bool value, string keyword)
        {
            materialEditor.RegisterPropertyChangeUndo(property.name);
            foreach (Object target in materialEditor.targets)
            {
                Material material = (Material)target;
                material.SetFloat(property.name, value ? 1f : 0f);
                CoreUtils.SetKeyword(material, keyword, value);
            }
        }

        private static void SetSpace(MaterialEditor materialEditor, MaterialProperty property, int index)
        {
            materialEditor.RegisterPropertyChangeUndo(property.name);
            foreach (Object target in materialEditor.targets)
            {
                Material material = (Material)target;
                material.SetFloat(property.name, index);
                ApplySpaceKeyword(material, index);
            }
        }
    }
}
