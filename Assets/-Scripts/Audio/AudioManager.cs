using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("List")]
    public List<AudioClip> clipList = new List<AudioClip>();

    [Header("Clicks")]
    public AudioClip pressed;
    public AudioClip released;
    public AudioClip complete;
    public AudioClip fail;

    [Header("Mixer")]
    [SerializeField] private AudioMixerGroup mixerGroup;

    private AudioSource audioSource;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            Debug.LogError("AudioManager: no AudioSource on " + gameObject.name);
    }

    void Start()
    {
        if (audioSource != null && mixerGroup != null)
            audioSource.outputAudioMixerGroup = mixerGroup;
    }

    public virtual void PlaySound(AudioClip clip)
    {
        if (audioSource == null || clip == null) return;
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
