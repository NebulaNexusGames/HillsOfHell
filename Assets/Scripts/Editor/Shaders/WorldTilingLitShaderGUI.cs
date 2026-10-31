using System;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarGame.Editor.Shaders
{
    public sealed class WorldTilingLitShaderGUI : BaseShaderGUI
    {
        private static readonly string[] workflowModeNames = Enum.GetNames(typeof(LitGUI.WorkflowMode));

        private static readonly GUIContent detailInputsHeader = EditorGUIUtility.TrTextContent("Detail Inputs",
            "These settings define the surface details by tiling and overlaying additional maps on the surface.");
        private static readonly GUIContent detailMaskText = EditorGUIUtility.TrTextContent("Mask");
        private static readonly GUIContent detailAlbedoText = EditorGUIUtility.TrTextContent("Base Map");
        private static readonly GUIContent detailNormalText = EditorGUIUtility.TrTextContent("Normal Map");
        private static readonly GUIContent detailScaleInfo = EditorGUIUtility.TrTextContent("Setting the scaling factor to a value other than 1 results in a less performant shader variant.");

        private LitGUI.LitProperties litProperties;
        private TilingProperties tilingProperties;

        private MaterialProperty detailMaskProp;
        private MaterialProperty detailAlbedoMapProp;
        private MaterialProperty detailAlbedoMapScaleProp;
        private MaterialProperty detailNormalMapProp;
        private MaterialProperty detailNormalMapScaleProp;

        public override void FillAdditionalFoldouts(MaterialHeaderScopeList materialScopesList)
        {
            materialScopesList.RegisterHeaderScope(detailInputsHeader, Expandable.Details, _ => DrawDetailArea());
        }

        public override void FindProperties(MaterialProperty[] properties)
        {
            base.FindProperties(properties);
            litProperties = new LitGUI.LitProperties(properties);
            detailMaskProp = FindProperty("_DetailMask", properties, false);
            detailAlbedoMapProp = FindProperty("_DetailAlbedoMap", properties, false);
            detailAlbedoMapScaleProp = FindProperty("_DetailAlbedoMapScale", properties, false);
            detailNormalMapProp = FindProperty("_DetailNormalMap", properties, false);
            detailNormalMapScaleProp = FindProperty("_DetailNormalMapScale", properties, false);
            tilingProperties = new TilingProperties(properties);
        }

        public override void ValidateMaterial(Material material)
        {
            SetMaterialKeywords(material, LitGUI.SetMaterialKeywords, SetDetailKeywords);
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

            if (litProperties.workflowMode != null)
                DoPopup(LitGUI.Styles.workflowModeText, litProperties.workflowMode, workflowModeNames);

            base.DrawSurfaceOptions(material);
        }

        public override void DrawSurfaceInputs(Material material)
        {
            base.DrawSurfaceInputs(material);
            LitGUI.Inputs(litProperties, materialEditor, material);
            DrawEmissionProperties(material, true);
            DrawTileOffset(materialEditor, baseMapProp);
            WorldTilingShaderGUIUtils.DrawTilingArea(materialEditor, tilingProperties);
        }

        public override void DrawAdvancedOptions(Material material)
        {
            if (litProperties.reflections != null && litProperties.highlights != null)
            {
                materialEditor.ShaderProperty(litProperties.highlights, LitGUI.Styles.highlightsText);
                materialEditor.ShaderProperty(litProperties.reflections, LitGUI.Styles.reflectionsText);
            }

            base.DrawAdvancedOptions(material);
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            SetupMaterialBlendMode(material);
            WorldTilingShaderGUIUtils.ApplyKeywords(material);
        }

        private void DrawDetailArea()
        {
            if (detailMaskProp != null)
                materialEditor.TexturePropertySingleLine(detailMaskText, detailMaskProp);

            if (detailAlbedoMapProp != null)
            {
                materialEditor.TexturePropertySingleLine(detailAlbedoText, detailAlbedoMapProp,
                    detailAlbedoMapProp.textureValue != null ? detailAlbedoMapScaleProp : null);

                if (detailAlbedoMapScaleProp != null && detailAlbedoMapScaleProp.floatValue != 1.0f)
                    EditorGUILayout.HelpBox(detailScaleInfo.text, MessageType.Info, true);
            }

            if (detailNormalMapProp != null)
            {
                materialEditor.TexturePropertySingleLine(detailNormalText, detailNormalMapProp,
                    detailNormalMapProp.textureValue != null ? detailNormalMapScaleProp : null);
            }

            if (detailAlbedoMapProp != null)
                materialEditor.TextureScaleOffsetProperty(detailAlbedoMapProp);
        }

        private static void SetDetailKeywords(Material material)
        {
            if (!material.HasProperty("_DetailAlbedoMap") || !material.HasProperty("_DetailNormalMap") || !material.HasProperty("_DetailAlbedoMapScale"))
                return;

            bool isScaled = material.GetFloat("_DetailAlbedoMapScale") != 1.0f;
            bool hasDetailMap = material.GetTexture("_DetailAlbedoMap") || material.GetTexture("_DetailNormalMap");
            CoreUtils.SetKeyword(material, "_DETAIL_MULX2", !isScaled && hasDetailMap);
            CoreUtils.SetKeyword(material, "_DETAIL_SCALED", isScaled && hasDetailMap);
        }
    }
}
