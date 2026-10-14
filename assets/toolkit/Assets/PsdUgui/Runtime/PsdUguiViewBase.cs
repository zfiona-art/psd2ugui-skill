using System;
using System.Collections;
using UnityEngine;

namespace PsdUgui
{
    // Standalone base; no UI manager, message bus or tween package required.
    public class PsdUguiViewBase : MonoBehaviour
    {
        [SerializeField] private bool animatePopup;
        [SerializeField] private float animationDuration = 0.2f;
        private Coroutine animationRoutine;
        private Transform content;
        private Vector3 restScale;

        protected virtual void Awake()
        {
            content = transform.Find("content");
            if (content != null) restScale = content.localScale;
        }

        public virtual void Show()
        {
            gameObject.SetActive(true);
            Animate(true, null);
        }

        public virtual void Hide(Action onComplete = null)
        {
            Animate(false, () => { gameObject.SetActive(false); onComplete?.Invoke(); });
        }

        private void Animate(bool opening, Action onComplete)
        {
            if (animationRoutine != null) StopCoroutine(animationRoutine);
            animationRoutine = null;
            if (!animatePopup || content == null || animationDuration <= 0 || !isActiveAndEnabled)
            {
                if (content != null) content.localScale = restScale;
                onComplete?.Invoke();
                return;
            }
            animationRoutine = StartCoroutine(ScalePopup(opening, onComplete));
        }

        private IEnumerator ScalePopup(bool opening, Action onComplete)
        {
            Vector3 from = opening ? Vector3.zero : content.localScale;
            Vector3 to = opening ? restScale : Vector3.zero;
            float elapsed = 0;
            while (elapsed < animationDuration)
            {
                content.localScale = Vector3.Lerp(from, to, Mathf.SmoothStep(0, 1, elapsed / animationDuration));
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            content.localScale = to;
            animationRoutine = null;
            onComplete?.Invoke();
        }

        protected virtual void OnDisable()
        {
            if (animationRoutine != null) StopCoroutine(animationRoutine);
            animationRoutine = null;
            if (content != null) content.localScale = restScale;
        }
    }
}
