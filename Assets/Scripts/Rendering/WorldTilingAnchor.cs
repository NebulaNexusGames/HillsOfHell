using UnityEngine;

namespace CarGame.Rendering
{
    [RequireComponent(typeof(Renderer))]
    public sealed class WorldTilingAnchor : MonoBehaviour
    {
        private static readonly int tilingAnchorMatrixId = Shader.PropertyToID("_TilingAnchorMatrix");

        private Renderer baseRenderer;
        private MaterialPropertyBlock propertyBlock;

        private void OnEnable()
        {
            if (baseRenderer == null)
                baseRenderer = GetComponent<Renderer>();
            if (propertyBlock == null)
                propertyBlock = new MaterialPropertyBlock();

            baseRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetMatrix(tilingAnchorMatrixId, transform.localToWorldMatrix);
            baseRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}