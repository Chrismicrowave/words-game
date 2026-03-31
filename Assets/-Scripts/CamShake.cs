using System.Collections;
using Unity.VisualScripting;
using UnityEngine;

public class CameraShakeAndZoom : MonoBehaviour
{
    private Vector3 originalPos;
    private Coroutine shakeCoroutine;

    public float mildShakeDuration = 0.5f;
    public float mildShakeMagnitude = 0.1f;

    public float strongShakeDuration = 1f;
    public float strongShakeMagnitude = 0.2f;

    public static CameraShakeAndZoom Instance { get; private set; }

    [Header ("Overzoom")]

    private Camera cam;
    private float startFOV = 60f;
    public float deg = 1f;
    public float spd = 10f;
    public float degExtra = 3f;

    void Awake()
    {
        originalPos = transform.localPosition;
    }

    void Start()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }

        cam = GetComponent<Camera>();
        cam.fieldOfView = startFOV; // Initialize camera FOV
    }


    public void OverZoomCam()
    {
        StartCoroutine(ZoomInAnim(deg, spd, degExtra));
    }


    IEnumerator ZoomInAnim(float deg, float spd, float degExtra)
    {
        float startFOV = cam.fieldOfView;

        // First: zoom in to overshoot (extra zoom)
        while (Mathf.Abs(startFOV - cam.fieldOfView) < degExtra)
        {
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, cam.fieldOfView - degExtra, spd * Time.deltaTime);
            yield return null;
        }

        // Then: zoom back to actual target FOV
        while (Mathf.Abs(startFOV - cam.fieldOfView) > deg)
        {
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, cam.fieldOfView + deg, spd * Time.deltaTime);
            yield return null;
        }

        cam.fieldOfView = cam.fieldOfView - deg; // Snap exactly to final value
    }

    public void ResetFOV()
    {
        StopAllCoroutines();
        cam.fieldOfView = startFOV; // Reset to default FOV
    }

    public void MildShake()
    {
        Shake(mildShakeDuration, mildShakeMagnitude);
    }
    public void StrongShake()
    {
        Shake(strongShakeDuration, strongShakeMagnitude);
    }


    public void Shake(float duration, float magnitude)
    {
        if (shakeCoroutine != null)
            StopCoroutine(shakeCoroutine);

        shakeCoroutine = StartCoroutine(ShakeCoroutine(duration, magnitude));
    }

    private IEnumerator ShakeCoroutine(float duration, float magnitude)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float x = Random.Range(-1f, 1f) * magnitude;
            float y = Random.Range(-1f, 1f) * magnitude;

            transform.localPosition = originalPos + new Vector3(x, y, 0f);

            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.localPosition = originalPos;
        shakeCoroutine = null;
    }
}
