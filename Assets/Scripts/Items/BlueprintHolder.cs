using System.Collections.Generic;
using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Точка, чьи дочерние объекты — чертежи-варианты одного слота (например,
    /// на машине: щит из скрапа / железа / платины на одном месте). Держатель
    /// ведёт список дочерних чертежей (Refresh) и знает про построенные
    /// (HasBuilt / HasBuiltSibling).
    ///
    /// Правило: пока один чертёж держателя построен, остальные его чертежи
    /// строить нельзя. Чертежи, не входящие в этого держателя, друг друга НЕ
    /// блокируют — их можно ставить вплотную и друг на друга.
    ///
    /// Вешается на Empty, внутри которого лежат чертежи.
    /// </summary>
    [DisallowMultipleComponent]
    public class BlueprintHolder : MonoBehaviour
    {
        private readonly List<Blueprint> blueprints = new List<Blueprint>();
        private bool cached;

        /// <summary>Есть ли среди дочерних чертежей построенный.</summary>
        public bool HasBuilt
        {
            get
            {
                Cache();
                for (int i = 0; i < blueprints.Count; i++)
                {
                    Blueprint blueprint = blueprints[i];
                    if (blueprint != null && blueprint.IsBuilt) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Построен ли ДРУГОЙ чертёж этого держателя (не считая самого
        /// <paramref name="except"/>). Так стопка вариантов одного слота не
        /// строится поверх уже стоящего собрата.
        /// </summary>
        public bool HasBuiltSibling(Blueprint except)
        {
            Cache();
            for (int i = 0; i < blueprints.Count; i++)
            {
                Blueprint blueprint = blueprints[i];
                if (blueprint != null && blueprint != except && blueprint.IsBuilt) return true;
            }
            return false;
        }

        /// <summary>Пересобрать список дочерних чертежей (если они менялись в рантайме).</summary>
        public void Refresh()
        {
            blueprints.Clear();
            blueprints.AddRange(GetComponentsInChildren<Blueprint>(true));
            cached = true;
        }

        private void Awake()
        {
            Refresh();
        }

        private void OnTransformChildrenChanged()
        {
            Refresh();
        }

        private void Cache()
        {
            if (!cached) Refresh();
        }
    }
}
