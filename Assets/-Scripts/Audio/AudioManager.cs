using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header ("List")]
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
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        if (mixerGroup != null) audioSource.outputAudioMixerGroup = mixerGroup;

        // Apply saved volume settings on start
        float master = PlayerPrefs.GetFloat(SettingsManager.KeyMasterVolume, 1f);
        float sfx    = PlayerPrefs.GetFloat(SettingsManager.KeySFXVolume,    1f);
        float bgm    = PlayerPrefs.GetFloat(SettingsManager.KeyBGMVolume,    1f);
        SetMasterVolume(master);
        SetSFXVolume(sfx);
        SetBGMVolume(bgm);
    }

    public void SetMasterVolume(float linear)
    {
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.MasterVolume = linear;
    }

    public void SetSFXVolume(float linear)
    {
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.SFXVolume = linear;
    }

    public void SetBGMVolume(float linear)
    {
        // BGM is stored; SettingsManager will apply when mixer param exists
        PlayerPrefs.SetFloat(SettingsManager.KeyBGMVolume, linear);
    }

    public virtual void PlaySound(AudioClip clip)
    {
        audioSource.clip = clip;

        if (!audioSource.isPlaying)
        { audioSource.Play(); }
    }

    public virtual void PlayAudioList(List<AudioClip> list)
    {
        int r = Random.Range(0, list.Count);

        if (!audioSource.isPlaying)
        { PlaySound(list[r]); }
    }

    public void ShiftPitch(float pitch)
    {
        audioSource.pitch = Random.Range(audioSource.pitch - pitch, audioSource.pitch + pitch);
    }

    public void AddPitch(float pitch)
    {
        audioSource.pitch += pitch;
        audioSource.volume +=0.1f;
    }

    public void ResetPitch()
    {
        audioSource.pitch = 1f;
        audioSource.volume = 1f;
    }

    public void SetVolume(float volume)
    {
        audioSource.volume = volume;
    }

    //public virtual void PlayDefault()
    //{
    //    audioSource.Play();
    //}
    public void StopAudio()
    {

        if (audioSource.isPlaying)
        { audioSource.Stop(); }
        else
        {
            return;
        }
    }
}
