using UnityEditor;
using UnityEngine;

namespace CarGame.EditorTools
{
    /// <summary>
    /// Старый пункт «Добавить виньетку игроку» ведёт теперь к единому
    /// менеджеру пост-процессинга: отдельные виньетки заменены одним
    /// <see cref="PostFXSetupMenu"/>, который обслуживает всю сцену.
    /// Оставлен только для тех, кто привык к старой команде меню.
    /// </summary>
    public static class VignetteSetupMenu
    {
        [MenuItem("Tools/CarGame/UI/Add Damage Vignette (player)")]
        public static void AddVignette()
        {
            PostFXSetupMenu.CreatePostFXManager();
        }
    }
}