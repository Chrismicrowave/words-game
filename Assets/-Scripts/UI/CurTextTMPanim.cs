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
    private float transitionStartTime;

    void Awake()
    {
        tmp = GetComponent<TextMeshProUGUI>();
        lastText = tmp.text;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        Initialize();
        StartTransition();
    }

    void Update()
    {
        if (tmp.text != lastText)
        {
            lastText = tmp.text;
            Initialize();
            StartTransition();
        }

        if (transitioning)
            MoveInLetters();
        else if (floating)
            FloatingLetters();
    }

    void Initialize()
    {
        tmp.ForceMeshUpdate();
        textInfo = tmp.textInfo;

        // Initialize letter landing tracker
        letterLanded = new bool[textInfo.characterCount];
        for (int i = 0; i < letterLanded.Length; i++)
            letterLanded[i] = false;

        int meshCount = textInfo.meshInfo.Length;
        targetVertexPositions = new Vector3[meshCount][];
        currentVertexPositions = new Vector3[meshCount][];

        for (int i = 0; i < meshCount; i++)
        {
            int vertLength = textInfo.meshInfo[i].vertices.Length;
            targetVertexPositions[i] = new Vector3[vertLength];
            currentVertexPositions[i] = new Vector3[vertLength];

            textInfo.meshInfo[i].vertices.CopyTo(targetVertexPositions[i], 0);

            // Initialize all vertex colors to transparent
            Color32[] colors = textInfo.meshInfo[i].colors32;
            for (int c = 0; c < colors.Length; c++)
                colors[c] = new Color32(colors[c].r, colors[c].g, colors[c].b, 0);

            for (int j = 0; j < vertLength; j++)
                currentVertexPositions[i][j] = targetVertexPositions[i][j] + offsetStartPosition;
        }
    }

    void StartTransition()
    {
        transitioning = true;
        floating = false;
        transitionStartTime = Time.time;
    }

    void MoveInLetters()
    {
        bool allArrived = true;
        float elapsed = Time.time - transitionStartTime;

        for (int m = 0; m < textInfo.meshInfo.Length; m++)
        {
            Vector3[] currentVerts = currentVertexPositions[m];
            Vector3[] targetVerts = targetVertexPositions[m];
            Color32[] vertexColors = textInfo.meshInfo[m].colors32;

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
                    audioSource.pitch = Random.Range(1f / landingSoundPitchRandomization, landingSoundPitchRandomization);
                    audioSource.PlayOneShot(landingSound, landingSoundVolume);
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

            textInfo.meshInfo[m].mesh.vertices = currentVerts;
            textInfo.meshInfo[m].mesh.colors32 = vertexColors;
            tmp.UpdateGeometry(textInfo.meshInfo[m].mesh, m);
        }

        if (allArrived)
        {
            transitioning = false;
            floating = true;
        }
    }

    void FloatingLetters()
    {
        float time = Time.time;

        for (int m = 0; m < textInfo.meshInfo.Length; m++)
        {
            Vector3[] verts = new Vector3[textInfo.meshInfo[m].vertices.Length];
            targetVertexPositions[m].CopyTo(verts, 0);

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

            textInfo.meshInfo[m].mesh.vertices = verts;
            tmp.UpdateGeometry(textInfo.meshInfo[m].mesh, m);
        }
    }
}