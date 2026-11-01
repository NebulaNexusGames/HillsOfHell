using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;

namespace CarGame.UI
{
    /// <summary>
    /// Экран смерти. Отдельная сцена: игра грузит её, когда игрок погибает.
    ///
    /// Весь экран держится на одном этом компоненте: он плавно мигает подсказкой
    /// и сам ловит нажатие клавиши перезапуска (по умолчанию R). Ни кнопок, ни
    /// отдельных скриптов не нужно — текст и клавиша живут здесь же.
    ///
    /// Экран строит инструмент Tools/CarGame/Menus/Create Death Screen.
    /// </summary>
    [DisallowMultipleComponent]
    public class DeathScreenUI : MonoBehaviour
    {
        [Header("Сцены")]
        [Tooltip("Игровая сцена, которую перезапускает клавиша")]
        [SerializeField] private string gameSceneName = "SampleScene";

        [Header("Текст")]
        [Tooltip("Надпись, которая плавно мигает")]
        [SerializeField] private TMP_Text restartText;
        [Tooltip("Текст подсказки (подставляется в надпись)")]
        [SerializeField] private string restartMessage = "Press R to Restart";
        [Tooltip("Базовый цвет текста; прозрачность мигает поверх него")]
        [SerializeField] private Color textColor = Color.white;
        [Tooltip("Сколько раз в секунду текст мигает")]
        [SerializeField] private float blinkSpeed = 1f;
        [Range(0f, 1f)]
        [Tooltip("Минимальная прозрачность текста при мигании")]
        [SerializeField] private float blinkMinAlpha = 0.2f;
        [Range(0f, 1f)]
        [Tooltip("Максимальная прозрачность текста при мигании")]
        [SerializeField] private float blinkMaxAlpha = 1f;

        [Header("Клавиша")]
        [Tooltip("Клавиша, по которой игра перезапускается")]
        [SerializeField] private Key restartKey = Key.R;

        private void Awake()
        {
            // Сцена смерти открывается поверх «замороженной» игры — время должно
            // идти, а курсор должен быть свободен, иначе ничего не оживёт.
            Time.timeScale = 1f;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            if (restartText != null)
            {
                restartText.color = textColor;
                if (!string.IsNullOrEmpty(restartMessage)) restartText.text = restartMessage;
            }
        }

        private void Update()
        {
            Blink();

            if (RestartPressed())
                Restart();
        }

        /// <summary>Плавное (синусоидальное) мигание подсказки.</summary>
        private void Blink()
        {
            if (restartText == null) return;

            float wave = 0.5f - 0.5f * Mathf.Cos(Time.unscaledTime * blinkSpeed * Mathf.PI * 2f);
            float alpha = Mathf.Lerp(blinkMinAlpha, blinkMaxAlpha, wave);
            restartText.color = new Color(textColor.r, textColor.g, textColor.b, alpha);
        }

        private bool RestartPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return false;

            KeyControl control = keyboard[restartKey];
            return control != null && control.wasPressedThisFrame;
        }

        /// <summary>Перезапускает игровую сцену.</summary>
        public void Restart()
        {
            if (string.IsNullOrEmpty(gameSceneName))
            {
                Debug.LogWarning("[DeathScreen] Не задано имя игровой сцены.", this);
                return;
            }

            Time.timeScale = 1f;
            SceneManager.LoadScene(gameSceneName);
        }
    }
}
