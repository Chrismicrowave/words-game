using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Words.UiMockups.WordListHub
{
    /// <summary>Owns at most one menu-click listener for a Hub Button.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class WordListHubButtonSoundRelay : MonoBehaviour
    {
        private Button button;
        private AudioSource source;
        private UnityAction ownedListener;
        private bool listenerOwned;

        public bool IsListening => listenerOwned;

        private void Awake()
        {
            button = GetComponent<Button>();
            ownedListener = PlayClick;
        }

        private void OnEnable()
        {
            EnsureListener();
        }

        private void OnDestroy()
        {
            RemoveListener();
        }

        public static WordListHubButtonSoundRelay Attach(Button target, AudioSource audioSource)
        {
            if (target == null || audioSource == null)
                return null;

            WordListHubButtonSoundRelay relay = target.GetComponent<WordListHubButtonSoundRelay>();
            if (relay == null)
                relay = target.gameObject.AddComponent<WordListHubButtonSoundRelay>();
            relay.SetAudioSource(audioSource);
            return relay;
        }

        public void SetAudioSource(AudioSource audioSource)
        {
            if (source == audioSource)
            {
                // Recover if a caller cleared runtime callbacks during a row rebuild.
                RemoveListener();
                EnsureListener();
                return;
            }

            RemoveListener();
            source = audioSource;
            if (button == null)
                button = GetComponent<Button>();
            if (ownedListener == null)
                ownedListener = PlayClick;
            EnsureListener();
        }

        private void EnsureListener()
        {
            if (button == null || source == null)
                return;

            // Persistent UnityEvents run before runtime listeners. Leave an active
            // Menu.Play event as the sole sound path; Off events do not count.
            if (HasPersistentMenuPlay())
            {
                RemoveListener();
                return;
            }
            if (listenerOwned)
                return;

            if (ownedListener == null)
                ownedListener = PlayClick;
            button.onClick.AddListener(ownedListener);
            listenerOwned = true;
        }

        private bool HasPersistentMenuPlay()
        {
            int count = button.onClick.GetPersistentEventCount();
            for (int index = 0; index < count; index++)
            {
                if (button.onClick.GetPersistentTarget(index) == source
                    && button.onClick.GetPersistentMethodName(index) == nameof(AudioSource.Play)
                    && button.onClick.GetPersistentListenerState(index) != UnityEventCallState.Off)
                    return true;
            }
            return false;
        }

        private void PlayClick()
        {
            if (button != null && button.isActiveAndEnabled && button.IsInteractable() && source != null)
                source.Play();
        }

        private void RemoveListener()
        {
            if (!listenerOwned)
                return;
            if (button == null)
                button = GetComponent<Button>();
            if (button != null && ownedListener != null)
                button.onClick.RemoveListener(ownedListener);
            listenerOwned = false;
        }
    }
}
