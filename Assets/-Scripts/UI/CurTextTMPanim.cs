using System;
using UnityEngine;
using TMPro;

[RequireComponent(typeof(TextMeshProUGUI))]
public class CurTextTMPanim : MonoBehaviour
{
    public Vector3 offsetStartPosition = new Vector3(0, 50, 0);
    public float transitionSpeed = 10f;
    public float delayBetweenLetters = 0.05f;
    public float curFloatAmplitude = 5f;
    public float curFloatSpeed = 2f;

    [Header("Audio Settings")]
    public AudioClip landingSound;
    [Range(0, 1)] public float landingSoundVolume = 0.5f;
    [Range(0.5f, 2f)] public float landingSoundPitchRandomization = 1.1f;
    private AudioSource audioSource;

    private TextMeshProUGUI tmp;
    private TMP_TextInfo textInfo;

    private const float LandingDetectionThreshold = 0.99f;

    private string lastText = "";
    private bool transitioning = false;
    private bool floating = false;
    private bool[] letterLanded;

    private Vector3[][] targetVertexPositions;
    private Vector3[][] currentVertexPositions;
    private Vector3[][] sourceVertexBuffers;
    private Color32[][] sourceColorBuffers;
    private Mesh[] sourceMeshes;
    private bool[] materialHasVisibleCharacters;
    private int animationMeshCount;
    private int initializedCharacterCount;
    private int initializedMaterialCount;
    private bool geometryRefreshPending;
    private float transitionStartTime;

    void Awake()
    {
        tmp = GetComponent<TextMeshProUGUI>();
        if (tmp == null)
            return;

        lastText = tmp.text;

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        AudioManager.TryRouteToSfx(audioSource);

        if (Initialize())
            StartTransition();
    }

    void Update()
    {
        if (tmp == null)
            return;

        bool geometryReady = false;
        if (tmp.text != lastText)
        {
            lastText = tmp.text;
            geometryReady = RebuildAndStartTransition();
        }
        else if (geometryRefreshPending)
            geometryReady = RebuildAndStartTransition();
        else if (transitioning || floating)
            geometryReady = HasCurrentAnimationGeometry()
                || RebuildAndStartTransition();

        if (!geometryReady)
            return;

        if (transitioning)
            MoveInLetters();
        else if (floating)
            FloatingLetters();
    }

    private bool RebuildAndStartTransition()
    {
        if (!Initialize())
            return false;

        StartTransition();
        return true;
    }

    private bool Initialize()
    {
        if (tmp == null)
        {
            ResetAnimationGeometry(false);
            return false;
        }

        tmp.ForceMeshUpdate();
        textInfo = tmp.textInfo;
        if (textInfo == null || textInfo.meshInfo == null)
        {
            ResetAnimationGeometry(!string.IsNullOrEmpty(tmp.text));
            return false;
        }

        if (textInfo.characterInfo == null)
        {
            ResetAnimationGeometry(ShouldRetryGeometry());
            return false;
        }

        if (textInfo.characterCount <= 0)
        {
            ResetAnimationGeometry(ShouldRetryGeometry());
            return false;
        }

        int meshCount = GetAnimationMeshCount();
        if (meshCount <= 0)
        {
            ResetAnimationGeometry(true);
            return false;
        }

        bool[] visibleMaterials = new bool[meshCount];
        int[] lastVisibleVertex = new int[meshCount];
        for (int m = 0; m < meshCount; m++)
            lastVisibleVertex[m] = -1;

        bool hasVisibleCharacters = false;
        for (int i = 0; i < textInfo.characterCount; i++)
        {
            TMP_CharacterInfo character = textInfo.characterInfo[i];
            if (!character.isVisible)
                continue;

            int materialIndex = character.materialReferenceIndex;
            int lastVertex = character.vertexIndex + 3;
            if (materialIndex < 0 || materialIndex >= meshCount || character.vertexIndex < 0)
            {
                ResetAnimationGeometry(true);
                return false;
            }

            visibleMaterials[materialIndex] = true;
            lastVisibleVertex[materialIndex] = Mathf.Max(lastVisibleVertex[materialIndex], lastVertex);
            hasVisibleCharacters = true;
        }

        if (!hasVisibleCharacters)
        {
            ResetAnimationGeometry(ShouldRetryGeometry());
            return false;
        }

        letterLanded = new bool[textInfo.characterCount];
        targetVertexPositions = new Vector3[meshCount][];
        currentVertexPositions = new Vector3[meshCount][];
        sourceVertexBuffers = new Vector3[meshCount][];
        sourceColorBuffers = new Color32[meshCount][];
        sourceMeshes = new Mesh[meshCount];
        materialHasVisibleCharacters = visibleMaterials;
        animationMeshCount = meshCount;
        initializedCharacterCount = textInfo.characterCount;
        initializedMaterialCount = textInfo.materialCount;

        for (int i = 0; i < meshCount; i++)
        {
            if (!visibleMaterials[i])
            {
                targetVertexPositions[i] = Array.Empty<Vector3>();
                currentVertexPositions[i] = Array.Empty<Vector3>();
                continue;
            }

            TMP_MeshInfo meshInfo = textInfo.meshInfo[i];
            Vector3[] vertices = meshInfo.vertices;
            Color32[] colors = meshInfo.colors32;
            if (meshInfo.mesh == null || vertices == null || colors == null
                || vertices.Length <= lastVisibleVertex[i] || colors.Length <= lastVisibleVertex[i])
            {
                ResetAnimationGeometry(true);
                return false;
            }

            int vertLength = vertices.Length;
            targetVertexPositions[i] = new Vector3[vertLength];
            currentVertexPositions[i] = new Vector3[vertLength];
            sourceVertexBuffers[i] = vertices;
            sourceColorBuffers[i] = colors;
            sourceMeshes[i] = meshInfo.mesh;

            vertices.CopyTo(targetVertexPositions[i], 0);

            // Initialize all vertex colors to transparent
            for (int c = 0; c < colors.Length; c++)
                colors[c] = new Color32(colors[c].r, colors[c].g, colors[c].b, 0);

            for (int j = 0; j < vertLength; j++)
                currentVertexPositions[i][j] = targetVertexPositions[i][j] + offsetStartPosition;
        }

        geometryRefreshPending = false;
        return true;
    }

    private int GetAnimationMeshCount()
    {
        if (textInfo == null || textInfo.meshInfo == null)
            return 0;

        int materialCount = textInfo.materialCount;
        if (materialCount <= 0 && textInfo.characterCount > 0)
            materialCount = textInfo.meshInfo.Length;

        return Mathf.Min(materialCount, textInfo.meshInfo.Length);
    }

    private bool HasCurrentAnimationGeometry()
    {
        if (tmp == null || textInfo == null || textInfo.meshInfo == null
            || textInfo.characterInfo == null || textInfo.characterCount != initializedCharacterCount
            || textInfo.materialCount != initializedMaterialCount
            || GetAnimationMeshCount() != animationMeshCount
            || animationMeshCount <= 0
            || targetVertexPositions == null || currentVertexPositions == null
            || sourceVertexBuffers == null || sourceColorBuffers == null || sourceMeshes == null
            || materialHasVisibleCharacters == null
            || targetVertexPositions.Length != animationMeshCount
            || currentVertexPositions.Length != animationMeshCount
            || sourceVertexBuffers.Length != animationMeshCount
            || sourceColorBuffers.Length != animationMeshCount
            || sourceMeshes.Length != animationMeshCount
            || materialHasVisibleCharacters.Length != animationMeshCount)
            return false;

        for (int i = 0; i < animationMeshCount; i++)
        {
            if (!materialHasVisibleCharacters[i])
                continue;

            TMP_MeshInfo meshInfo = textInfo.meshInfo[i];
            if (meshInfo.mesh == null || meshInfo.vertices == null || meshInfo.colors32 == null
                || targetVertexPositions[i] == null || currentVertexPositions[i] == null
                || !ReferenceEquals(meshInfo.vertices, sourceVertexBuffers[i])
                || !ReferenceEquals(meshInfo.colors32, sourceColorBuffers[i])
                || meshInfo.mesh != sourceMeshes[i]
                || meshInfo.vertices.Length != targetVertexPositions[i].Length
                || meshInfo.colors32.Length < targetVertexPositions[i].Length
                || currentVertexPositions[i].Length != targetVertexPositions[i].Length)
                return false;
        }

        return true;
    }

    private void ResetAnimationGeometry(bool retryWhenTextIsPresent)
    {
        letterLanded = Array.Empty<bool>();
        targetVertexPositions = Array.Empty<Vector3[]>();
        currentVertexPositions = Array.Empty<Vector3[]>();
        sourceVertexBuffers = Array.Empty<Vector3[]>();
        sourceColorBuffers = Array.Empty<Color32[]>();
        sourceMeshes = Array.Empty<Mesh>();
        materialHasVisibleCharacters = Array.Empty<bool>();
        animationMeshCount = 0;
        initializedCharacterCount = 0;
        initializedMaterialCount = 0;
        transitioning = false;
        floating = false;
        geometryRefreshPending = retryWhenTextIsPresent && ShouldRetryGeometry();
    }

    private bool ShouldRetryGeometry()
    {
        return tmp != null && !string.IsNullOrWhiteSpace(tmp.text);
    }

    void StartTransition()
    {
        transitioning = true;
        floating = false;
        transitionStartTime = Time.time;
    }

    void MoveInLetters()
    {
        if (!HasCurrentAnimationGeometry())
        {
            geometryRefreshPending = true;
            transitioning = false;
            floating = false;
            return;
        }

        bool allArrived = true;
        float elapsed = Time.time - transitionStartTime;

        for (int m = 0; m < animationMeshCount; m++)
        {
            if (!materialHasVisibleCharacters[m])
                continue;

            Vector3[] currentVerts = currentVertexPositions[m];
            Vector3[] targetVerts = targetVertexPositions[m];
            TMP_MeshInfo meshInfo = textInfo.meshInfo[m];
            Color32[] vertexColors = meshInfo.colors32;

            for (int i = 0; i < textInfo.characterCount; i++)
            {
                TMP_CharacterInfo charInfo = textInfo.characterInfo[i];
                if (!charInfo.isVisible || charInfo.materialReferenceIndex != m)
                    continue;

                int vertexIndex = charInfo.vertexIndex;
                float charDelay = i * delayBetweenLetters;

                if (elapsed < charDelay)
                {
                    for (int v = 0; v < 4; v++)
                    {
                        currentVerts[vertexIndex + v] = targetVerts[vertexIndex + v] + offsetStartPosition;
                        vertexColors[vertexIndex + v].a = 0;
                    }
                    allArrived = false;
                    continue;
                }

                float t = Mathf.Clamp01((elapsed - charDelay) * transitionSpeed);

                // Check if letter just landed
                if (t >= LandingDetectionThreshold && !letterLanded[i] && landingSound != null)
                {
                    letterLanded[i] = true;
                    if (audioSource != null && AudioManager.TryRouteToSfx(audioSource))
                    {
                        audioSource.pitch = UnityEngine.Random.Range(1f / landingSoundPitchRandomization, landingSoundPitchRandomization);
                        audioSource.PlayOneShot(landingSound, landingSoundVolume);
                    }
                }

                for (int v = 0; v < 4; v++)
                {
                    Vector3 startPos = targetVerts[vertexIndex + v] + offsetStartPosition;
                    currentVerts[vertexIndex + v] = Vector3.Lerp(startPos, targetVerts[vertexIndex + v], t);
                    vertexColors[vertexIndex + v].a = (byte)(t * 255);

                    if ((currentVerts[vertexIndex + v] - targetVerts[vertexIndex + v]).sqrMagnitude > 0.001f)
                        allArrived = false;
                }
            }

            meshInfo.mesh.vertices = currentVerts;
            meshInfo.mesh.colors32 = vertexColors;
            tmp.UpdateGeometry(meshInfo.mesh, m);
        }

        if (allArrived)
        {
            transitioning = false;
            floating = true;
        }
    }

    void FloatingLetters()
    {
        if (!HasCurrentAnimationGeometry())
        {
            geometryRefreshPending = true;
            transitioning = false;
            floating = false;
            return;
        }

        float time = Time.time;

        for (int m = 0; m < animationMeshCount; m++)
        {
            if (!materialHasVisibleCharacters[m])
                continue;

            TMP_MeshInfo meshInfo = textInfo.meshInfo[m];
            Vector3[] targetVerts = targetVertexPositions[m];
            Vector3[] verts = new Vector3[targetVerts.Length];
            targetVerts.CopyTo(verts, 0);

            for (int i = 0; i < textInfo.characterCount; i++)
            {
                TMP_CharacterInfo charInfo = textInfo.characterInfo[i];
                if (!charInfo.isVisible || charInfo.materialReferenceIndex != m)
                    continue;

                int vi = charInfo.vertexIndex;
                Vector3 floatOffset = new Vector3(0, Mathf.Sin(time * curFloatSpeed + i) * curFloatAmplitude, 0);

                verts[vi + 0] += floatOffset;
                verts[vi + 1] += floatOffset;
                verts[vi + 2] += floatOffset;
                verts[vi + 3] += floatOffset;
            }

            meshInfo.mesh.vertices = verts;
            tmp.UpdateGeometry(meshInfo.mesh, m);
        }
    }
}
