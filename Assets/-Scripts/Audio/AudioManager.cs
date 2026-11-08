using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class AudioManager : MonoBehaviour
{
    [Header("List")]
    [SerializeField] private List<AudioClip> clipList = new List<AudioClip>();

    [Header("Clicks")]
    public AudioClip pressed;
    public AudioClip released;
    public AudioClip complete;
    public AudioClip fail;

    [Header("Mixer")]
    [SerializeField] private AudioMixerGroup mixerGroup;

    private AudioSource audioSource;

    private static AudioMixerGroup sharedSfxMixerGroup;

    public static bool TryRouteToSfx(AudioSource source)
    {
        if (source == null)
            return false;
        if (source.outputAudioMixerGroup != null)
            return true;

        AudioMixerGroup group = sharedSfxMixerGroup;
        if (group == null)
        {
            AudioManager manager = FindAnyObjectByType<AudioManager>(FindObjectsInactive.Include);
            if (manager != null && manager.mixerGroup != null)
            {
                group = manager.mixerGroup;
                sharedSfxMixerGroup = group;
            }
        }
        if (group == null)
            return false;

        source.outputAudioMixerGroup = group;
        return source.outputAudioMixerGroup != null;
    }

    void Awake()
    {
        if (mixerGroup != null)
            sharedSfxMixerGroup = mixerGroup;

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            Debug.LogError("AudioManager: no AudioSource on " + gameObject.name);
            return;
        }

        TryRouteToSfx(audioSource);
    }

    void Start()
    {
        if (audioSource != null && !TryRouteToSfx(audioSource))
            Debug.LogWarning("AudioManager: no SFX mixer group is available; sounds will remain silent.", this);
    }

    void OnDestroy()
    {
        if (object.ReferenceEquals(sharedSfxMixerGroup, mixerGroup))
            sharedSfxMixerGroup = null;
    }

    public virtual void PlaySound(AudioClip clip)
    {
        if (audioSource == null || clip == null || !TryRouteToSfx(audioSource)) return;
        audioSource.clip = clip;
        if (!audioSource.isPlaying) audioSource.Play();
    }

    public virtual void PlayAudioList(List<AudioClip> list)
    {
        if (audioSource == null || list == null || list.Count == 0) return;
        int r = Random.Range(0, list.Count);
        if (!audioSource.isPlaying) PlaySound(list[r]);
    }

    public void ShiftPitch(float pitch)
    {
        if (audioSource == null) return;
        audioSource.pitch = Random.Range(audioSource.pitch - pitch, audioSource.pitch + pitch);
    }

    public void AddPitch(float pitch)
    {
        if (audioSource == null) return;
        audioSource.pitch += pitch;
        audioSource.volume += 0.1f;
    }

    public void ResetPitch()
    {
        if (audioSource == null) return;
        audioSource.pitch = 1f;
        audioSource.volume = 1f;
    }

    public void SetVolume(float volume)
    {
        if (audioSource == null) return;
        audioSource.volume = volume;
    }

    public void StopAudio()
    {
        if (audioSource == null) return;
        if (audioSource.isPlaying) audioSource.Stop();
    }
}
