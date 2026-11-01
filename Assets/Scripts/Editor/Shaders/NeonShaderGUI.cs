using UnityEditor;
using UnityEngine;

namespace CarGame.Editor.Shaders
{
    public sealed class NeonShaderGUI : BaseShaderGUI
    {
        private MaterialProperty[] neonProperties;

        public override void FindProperties(MaterialProperty[] properties)
        {
            base.FindProperties(properties);
            neonProperties = properties;
        }

        public override void ValidateMaterial(Material material)
        {
            SetMaterialKeywords(material);
        }

        public override void DrawSurfaceOptions(Material material)
        {
            EditorGUIUtility.labelWidth = 0f;
            base.DrawSurfaceOptions(material);
        }

        public override void DrawSurfaceInputs(Material material)
        {
            base.DrawSurfaceInputs(material);
            materialEditor.PropertiesDefaultGUI(neonProperties);
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            SetupMaterialBlendMode(material);
        }
    }
}
