using CarGame.Player;
using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Общая часть фонариков, которые держат в руке. Находит источник света в
    /// варианте руки, один раз задаёт ему цвет/дальность и умеет гасить его,
    /// когда предмет убрали из руки. Наследники решают только, как заряд
    /// превращается в яркость: обычный фонарик просто включается кнопкой,
    /// светотряс копит заряд тряской.
    ///
    /// Модель руки — это картинка, свет живёт на ней же. Ни объект варианта, ни
    /// его модель никогда не удаляются (см. AGENTS.md): свет лишь выключается
    /// вместе с вариантом и включается снова, когда игрок подберёт предмет.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class HandLightItem : MonoBehaviour, IHandUsable
    {
        [Header("Свет")]
        [Tooltip("Источник света. Пусто — ищется в варианте руки при подборе")]
        [SerializeField] private Light lightSource;
        [Tooltip("Яркость луча при полном заряде")]
        [SerializeField] private float maxIntensity = 4.5f;
        [Tooltip("Дальность луча, м")]
        [SerializeField] private float maxRange = 24f;
        [Tooltip("Цвет луча")]
        [SerializeField] private Color beamColor = new Color(1f, 0.95f, 0.8f, 1f);

        private Light resolvedBeam;
        private PlayerHand hand;

        /// <summary>Источник света варианта руки (может быть null, если его нет).</summary>
        protected Light Beam
        {
            get
            {
                ResolveBeam();
                return resolvedBeam;
            }
        }

        /// <summary>Рука, в которой сейчас лежит предмет.</summary>
        protected PlayerHand Hand => hand;

        /// <summary>Яркость при заряде 1.</summary>
        protected float MaxIntensity => Mathf.Max(0f, maxIntensity);

        public void OnUsePressed(PlayerHand current)
        {
            hand = current;
            ResolveBeam();
            ConfigureBeam();
            OnPressed(current);
        }

        public void OnUseHeld(PlayerHand current)
        {
            hand = current;
            OnHeld(current);
        }

        public void OnUseReleased(PlayerHand current)
        {
            hand = current;
            OnReleased(current);
        }

        public void OnUseCanceled()
        {
            // Луч не гасим здесь: вариант руки выключается сам (OnDisable), и
            // именно он тушит свет. Если гасить в OnUseCanceled, то меню (которое
            // зовёт OnUseCanceled каждый кадр) и смена слота рассинхронизировали бы
            // «горит» и «светит» — фонарик пришлось бы включать дважды.
            OnCanceled();
        }

        public void OnEquipped(PlayerHand current)
        {
            hand = current;
            ResolveBeam();
            ConfigureBeam();
            OnEquippedInternal(current);
        }

        /// <summary>Предмет только что взят в руку.</summary>
        protected abstract void OnEquippedInternal(PlayerHand current);

        /// <summary>ЛКМ нажата.</summary>
        protected abstract void OnPressed(PlayerHand current);

        /// <summary>ЛКМ удерживается.</summary>
        protected virtual void OnHeld(PlayerHand current) { }

        /// <summary>ЛКМ отпущена.</summary>
        protected virtual void OnReleased(PlayerHand current) { }

        /// <summary>
        /// Предмет убирают из руки (смена слота, меню, посадка в машину).
        /// Луч гасит <see cref="OnDisable"/> — здесь только сброс состояния.
        /// </summary>
        protected virtual void OnCanceled() { }

        protected virtual void OnDisable()
        {
            // Вариант руки выключается, когда предмет убрали или игрок сел в
            // машину. Свет обязан погаснуть вместе с ним, иначе луч остался бы
            // висеть в воздухе.
            if (resolvedBeam != null) resolvedBeam.enabled = false;
        }

        /// <summary>Задаёт яркость от 0 (выключен) до 1 (максимум).</summary>
        protected void ApplyBeam(float intensity01)
        {
            ResolveBeam();
            if (resolvedBeam == null) return;

            float value = Mathf.Clamp01(intensity01) * MaxIntensity;
            resolvedBeam.enabled = value > 0.01f;
            resolvedBeam.intensity = value;
        }

        /// <summary>Готовит источник света один раз — при подборе предмета.</summary>
        protected void ConfigureBeam()
        {
            ResolveBeam();
            if (resolvedBeam == null) return;

            resolvedBeam.color = beamColor;
            resolvedBeam.range = Mathf.Max(0.1f, maxRange);
            resolvedBeam.enabled = false;
            resolvedBeam.intensity = 0f;
        }

        private void ResolveBeam()
        {
            if (resolvedBeam != null) return;
            if (lightSource != null)
            {
                resolvedBeam = lightSource;
                return;
            }

            resolvedBeam = GetComponentInChildren<Light>(true);
        }
    }
}
