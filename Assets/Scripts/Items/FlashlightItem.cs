using CarGame.Player;
using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Обычный фонарик: светит сразу, ЛКМ включает и выключает луч. Аниматор ему
    /// не нужен — переключение видно по самому лучу. Звук, как и у остальных
    /// предметов, если понадобится, ставится анимацией позже, а не кодом.
    /// </summary>
    [DisallowMultipleComponent]
    public class FlashlightItem : HandLightItem
    {
        [Header("Фонарик")]
        [Tooltip("Гореть сразу после первого подбора")]
        [SerializeField] private bool startsOn;

        private bool isOn;
        private bool initialized;

        /// <summary>
        /// Первый подбор задаёт исходное состояние (по startsOn), а дальше
        /// состояние сохраняется между слотами: убрал фонарик — он не гаснет в
        /// «памяти», вернул в руку — снова горит так же, как горел. Луч гасит
        /// OnDisable при выключенном варианте руки.
        /// </summary>
        protected override void OnEquippedInternal(PlayerHand current)
        {
            if (!initialized)
            {
                isOn = startsOn;
                initialized = true;
            }

            ApplyBeam(isOn ? 1f : 0f);
        }

        /// <summary>Луч сейчас горит.</summary>
        public bool IsOn => isOn;

        protected override void OnPressed(PlayerHand current)
        {
            isOn = !isOn;
            ApplyBeam(isOn ? 1f : 0f);
        }
    }
}
