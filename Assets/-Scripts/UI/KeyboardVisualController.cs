// Assets/-Scripts/UI/KeyboardVisualController.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class KeyboardVisualController : MonoBehaviour
{
    [Serializable]
    public struct KeyMapping
    {
        public KeyCode keyCode;
        public Image keyImage;
    }

    [SerializeField] private List<KeyMapping> keyMappings = new List<KeyMapping>();
    [SerializeField] private Color defaultColor = Color.white;
    [SerializeField] private Color holdColor = Color.yellow;

    private Dictionary<KeyCode, Image> keyLookup = new Dictionary<KeyCode, Image>();

    void Awake()
    {
        foreach (var mapping in keyMappings)
        {
            if (mapping.keyImage != null)
                keyLookup[mapping.keyCode] = mapping.keyImage;
        }
    }

    void OnEnable()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStepProcessed += HandleStepProcessed;
            GameStateManager.Instance.OnPhaseCompleted += ResetAllKeys;
            GameStateManager.Instance.OnPhaseRestarted += ResetAllKeys;
            GameStateManager.Instance.OnGameReset += ResetAllKeys;
        }
    }

    void OnDisable()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStepProcessed -= HandleStepProcessed;
            GameStateManager.Instance.OnPhaseCompleted -= ResetAllKeys;
            GameStateManager.Instance.OnPhaseRestarted -= ResetAllKeys;
            GameStateManager.Instance.OnGameReset -= ResetAllKeys;
        }
    }

    void Start()
    {
        OnDisable();
        OnEnable();
    }

    private void HandleStepProcessed(StepResult result, Step step)
    {
        if (result == StepResult.Failed) return;

        if (step.Action == StepAction.Hold)
            SetKeyColor(step.Key, holdColor);
        else
            SetKeyColor(step.Key, defaultColor);
    }

    public void SetKeyColor(KeyCode key, Color color)
    {
        if (keyLookup.TryGetValue(key, out Image img))
            img.color = color;
    }

    public void ResetAllKeys()
    {
        foreach (var kvp in keyLookup)
            kvp.Value.color = defaultColor;
    }

    public void FlashKey(KeyCode key, Color color, float duration = 0.15f)
    {
        StartCoroutine(FlashKeyCoroutine(key, color, duration));
    }

    private IEnumerator FlashKeyCoroutine(KeyCode key, Color color, float duration)
    {
        SetKeyColor(key, color);
        yield return new WaitForSeconds(duration);
        SetKeyColor(key, defaultColor);
    }
}
