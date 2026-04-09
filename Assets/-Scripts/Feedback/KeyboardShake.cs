using System.Collections;
using UnityEngine;

public class KeyboardShake : MonoBehaviour
{
    [Header("Shake Settings")]
    [SerializeField] private bool isShaking = false;
    [SerializeField] private float magnitudeStart = 3f;

    private float magnitude;

    [SerializeField] private float speed = 20f;
    [SerializeField] private float increment = 2f;

    private Vector3 originalPos;
    private float shakeTimer = 0f;

    public static KeyboardShake Instance { get; private set; }

    void Start()
    {
        originalPos = transform.localPosition;
        magnitude = magnitudeStart;

        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Update()
    {
        if (isShaking)
        {
            shakeTimer += Time.deltaTime * speed;

            float offsetX = Mathf.PerlinNoise(shakeTimer, 0f) * 2f - 1f;
            float offsetY = Mathf.PerlinNoise(0f, shakeTimer) * 2f - 1f;

            transform.localPosition = originalPos + new Vector3(offsetX, offsetY, 0f) * magnitude;
        }
        else
        {
            transform.localPosition = originalPos;
        }
    }

    public void SetShaking(bool shake)
    {
        isShaking = shake;

        if (!shake)
            transform.localPosition = originalPos;
    }


    public void UpMagnitude()
    {
        if (isShaking)
        {
            magnitude += increment;
            StartCoroutine(SuddenShake());
        }
    }

    public void DownMagnitude()
    {
        if (isShaking)
        magnitude -= increment;
    }

    public void ResetMagnitude()
    {
        magnitude = magnitudeStart;
    }

    private IEnumerator SuddenShake()
    {
        float originalMagnitude = magnitude;
        magnitude = 20f; // Sudden shake magnitude
        yield return new WaitForSeconds(0.1f); // Duration of the sudden shake
        magnitude = originalMagnitude; // Reset to original magnitude
    }
}
