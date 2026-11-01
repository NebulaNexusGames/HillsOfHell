using UnityEditor;
using UnityEngine;

namespace CarGame.Editor.Shaders
{
    /// <summary>
    /// Инспектор 9-слайс шейдера. Обводка и заливка настраиваются отдельно:
    /// прозрачность обводки задаётся своими свойствами (_FrameColor/_FrameAlpha)
    /// и не зависит от заливки. Поэтому заливку можно сделать прозрачной, а
    /// обводку оставить как есть.
    /// </summary>
    public sealed class NineSliceShaderGUI : ShaderGUI
    {
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            EditorGUIUtility.labelWidth = 0f;

            Draw(properties, materialEditor, "_SliceTex", "_Border");
            Draw(properties, materialEditor, "_Color", "_Brightness", "_TintFrame");

            EditorGUILayout.Space();
            Header("Обводка (рамка)");
            Draw(properties, materialEditor, "_FrameColor", "_FrameAlpha");

            EditorGUILayout.Space();
            Header("Заливка (интерьер)");
            Draw(properties, materialEditor, "_Fill", "_FillAlpha", "_FillInset", "_FillRadius");

            EditorGUILayout.Space();
            Header("Stencil");
            Draw(properties, materialEditor,
                "_Stencil", "_StencilComp", "_StencilOp",
                "_StencilReadMask", "_StencilWriteMask", "_ColorMask");
        }

        private static void Header(string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private static void Draw(MaterialProperty[] properties, MaterialEditor editor, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                MaterialProperty property = FindProperty(names[i], properties, false);
                if (property != null) editor.ShaderProperty(property, property.displayName);
            }
        }
    }
}
