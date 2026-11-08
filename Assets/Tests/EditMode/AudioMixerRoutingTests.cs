using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

public sealed class AudioMixerRoutingTests
{
    private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

    private GameObject managerObject;
    private GameObject sourceObject;
    private AudioMixerGroup sfxGroup;
    private AudioMixerGroup bgmGroup;
    private MethodInfo routeToSfx;
    private FieldInfo cachedSfxGroup;
    private object previousSharedSfxGroup;

    [SetUp]
    public void SetUp()
    {
        AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/-Audio/GameMixer.mixer");
        Assert.IsNotNull(mixer, "The shared GameMixer asset should be available in EditMode.");

        AudioMixerGroup[] sfxGroups = mixer.FindMatchingGroups("SFX");
        AudioMixerGroup[] bgmGroups = mixer.FindMatchingGroups("BGM");
        Assert.IsNotEmpty(sfxGroups);
        Assert.IsNotEmpty(bgmGroups);
        sfxGroup = sfxGroups[0];
        bgmGroup = bgmGroups[0];

        System.Type audioManagerType = System.Type.GetType("AudioManager, Assembly-CSharp", true);
        routeToSfx = audioManagerType.GetMethod("TryRouteToSfx", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(routeToSfx);
        cachedSfxGroup = audioManagerType.GetField("sharedSfxMixerGroup", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(cachedSfxGroup);
        previousSharedSfxGroup = cachedSfxGroup.GetValue(null);
        cachedSfxGroup.SetValue(null, null);

        managerObject = new GameObject("Audio mixer routing test manager");
        managerObject.SetActive(false);
        managerObject.AddComponent<AudioSource>();
        Component manager = managerObject.AddComponent(audioManagerType);
        audioManagerType.GetField("mixerGroup", InstancePrivate).SetValue(manager, sfxGroup);
    }

    [TearDown]
    public void TearDown()
    {
        if (sourceObject != null)
            Object.DestroyImmediate(sourceObject);
        if (managerObject != null)
            Object.DestroyImmediate(managerObject);
        if (cachedSfxGroup != null)
            cachedSfxGroup.SetValue(null, previousSharedSfxGroup);
    }

    [Test]
    public void TryRouteToSfxAssignsTheMixerBeforeAnyPlayback()
    {
        sourceObject = new GameObject("Unrouted test source");
        AudioSource source = sourceObject.AddComponent<AudioSource>();

        bool routed = (bool)routeToSfx.Invoke(null, new object[] { source });

        Assert.IsTrue(routed);
        Assert.AreSame(sfxGroup, source.outputAudioMixerGroup);
    }

    [Test]
    public void TryRouteToSfxPreservesAnExplicitMixerGroup()
    {
        sourceObject = new GameObject("Explicit BGM route test source");
        AudioSource source = sourceObject.AddComponent<AudioSource>();
        source.outputAudioMixerGroup = bgmGroup;

        bool routed = (bool)routeToSfx.Invoke(null, new object[] { source });

        Assert.IsTrue(routed);
        Assert.AreSame(bgmGroup, source.outputAudioMixerGroup);
    }
}
