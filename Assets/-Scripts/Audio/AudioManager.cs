using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class AudioManager : MonoBehaviour
{
    [Header ("List")]
    public List<AudioClip> clipList = new List<AudioClip>();


    [Header("Clicks")]
    public AudioClip pressed;
    public AudioClip released;
    public AudioClip complete;
    public AudioClip fail;


    private AudioSource audioSource;

    // Start is called before the first frame update
    void Start()
    {
        audioSource = GetComponent<AudioSource>();
    }

    // Update is called once per frame
    void Update()
    {
       
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
