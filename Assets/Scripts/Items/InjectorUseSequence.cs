using System;
using System.Collections;
using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Невидимый временный раннер, который доводит применение инжектора до конца.
    /// Нужен потому, что в момент применения выключены и world-объект
    /// (hideSourceWhileHeld), и модель в руке — на обоих не работает Update,
    /// поэтому таймеры просто некому крутить.
    /// </summary>
    [DisallowMultipleComponent]
    public class InjectorUseSequence : MonoBehaviour
    {
        /// <summary>
        /// Создаёт раннер и запускает применение. onHeal — момент восстановления хп,
        /// onCompleted — момент, когда инжектор считается использованным.
        /// </summary>
        public static void Start(Vector3 position, float healDelay, float usedDelay, Action onHeal, Action onCompleted)
        {
            GameObject runner = new GameObject("InjectorUseSequence");
            runner.transform.position = position;

            InjectorUseSequence sequence = runner.AddComponent<InjectorUseSequence>();
            sequence.Run(healDelay, usedDelay, onHeal, onCompleted);
        }

        private void Run(float healDelay, float usedDelay, Action onHeal, Action onCompleted)
        {
            StartCoroutine(Play(healDelay, usedDelay, onHeal, onCompleted));
        }

        private IEnumerator Play(float healDelay, float usedDelay, Action onHeal, Action onCompleted)
        {
            // Бессмысленно, если инжектор исчезает раньше, чем подействует лечение.
            float total = Mathf.Max(usedDelay, healDelay);

            if (healDelay > 0f)
            {
                yield return new WaitForSeconds(healDelay);
            }
            else
            {
                // Хп всё равно начисляем кадром позже, а не внутри нажатия.
                yield return null;
            }

            if (onHeal != null) onHeal();

            float remaining = total - healDelay;
            if (remaining > 0f) yield return new WaitForSeconds(remaining);

            if (onCompleted != null) onCompleted();

            Destroy(gameObject);
        }
    }
}