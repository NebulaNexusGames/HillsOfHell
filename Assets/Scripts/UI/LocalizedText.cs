using CarGame.Controls;
using TMPro;
using UnityEngine;

namespace CarGame.UI
{
    /// <summary>
    /// Постоянная надпись, которая меняется вместе с языком. Хранит русский и
    /// английский варианты и подписывается на <see cref="Localization.Changed"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Text))]
    public class LocalizedText : MonoBehaviour
    {
        [TextArea, SerializeField] private string russian;
        [TextArea, SerializeField] private string english;

        private TMP_Text label;

        public string Russian => russian;
        public string English => english;

        public void Set(string ru, string en)
        {
            russian = ru;
            english = en;
            Apply();
        }

        private void OnEnable()
        {
            Localization.Changed += Apply;
            Apply();
        }

        private void OnDisable()
        {
            Localization.Changed -= Apply;
        }

        private void Apply()
        {
            if (label == null) label = GetComponent<TMP_Text>();
            if (label != null) label.text = Localization.Pick(russian, english);
        }
    }
}
