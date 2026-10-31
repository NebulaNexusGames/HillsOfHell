using UnityEditor;
using UnityEngine;

namespace CarGame.Editor.Shaders
{
    public sealed class WorldTilingUnlitShaderGUI : BaseShaderGUI
    {
        private TilingProperties tilingProperties;

        public override void FindProperties(MaterialProperty[] properties)
        {
            base.FindProperties(properties);
            tilingProperties = new TilingProperties(properties);
        }

        public override void ValidateMaterial(Material material)
        {
            SetMaterialKeywords(material);
            WorldTilingShaderGUIUtils.ApplyKeywords(material);
        }

        public override void OnOpenGUI(Material material, MaterialEditor materialEditor)
        {
            base.OnOpenGUI(material, materialEditor);
            WorldTilingShaderGUIUtils.ApplyKeywords(material);
        }

        public override void DrawSurfaceOptions(Material material)
        {
            EditorGUIUtility.labelWidth = 0f;
            base.DrawSurfaceOptions(material);
        }

        public override void DrawSurfaceInputs(Material material)
        {
            base.DrawSurfaceInputs(material);
            DrawTileOffset(materialEditor, baseMapProp);
            WorldTilingShaderGUIUtils.DrawTilingArea(materialEditor, tilingProperties);
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            SetupMaterialBlendMode(material);
            WorldTilingShaderGUIUtils.ApplyKeywords(material);
        }
    }
}
