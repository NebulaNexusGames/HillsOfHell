using UnityEngine;

namespace CarGame.Effects
{
    /// <summary>
    /// Метка на камере, которая не должна получать ни один пост-эффект рендерера
    /// (PSX/VHS, всполохи, винье́тка урона). Так рисуются иконки предметов:
    /// кадр превью обязан быть чистым, без пост-обработки.
    ///
    /// Проходы проверяют эту метку в <c>AddRenderPasses</c> и пропускают камеру.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PreviewRenderExclusion : MonoBehaviour
    {
    }
}
