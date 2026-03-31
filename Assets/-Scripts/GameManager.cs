using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.UIElements.Experimental;

public class GameManager : MonoBehaviour
{
    public List<string> targetTexts = new List<string> 
    {
        "No Food"
    };
    string targetText;
    string matchedText;
    string notMatchedText;

    public TextMeshProUGUI targetTextUI;
    public TextMeshProUGUI matchedTextUI;
    public TextMeshProUGUI notMatchedTextUI;

    public bool showActionPrompt = true;
    bool doneFirstPhase = false;


    [Header("Cursor Blink")]
    private float blinkTimer = 0f;
    private bool showCursor = true;
    public float cursorBlinkInterval = 0.5f;
    private readonly string blockChar = "<size=75%>\u2588</size>"; // Full block


    [Header ("Audio")]
    public AudioManager audioKeys;
    public AudioManager audioResult;

    private enum StepAction { Hold, Release }

    public Color keyReleaseColor;
    public Color keyHoldColor;



    [Header("Phase Scroll View")]
    public TMP_InputField inputField;
    public Color inputFieldActiveColor;
    public Color inputFieldInactiveColor;

    public ScrollRect scrollRect;
    public TextMeshProUGUI scrollText;

    public Color phaseUnselected;
    public Color phaseSelected;
    [SerializeField] private Transform scrollContent; 
    [SerializeField] private GameObject phaseButtonPrefab; 

    private int selectedPhaseIndex = -1;



    [Header("Timer")]
    public TextMeshProUGUI curPhaseTime;
    public TextMeshProUGUI totalTime;

    private float phaseStartTime;
    private float currentPhaseDuration;
    private float totalElapsedTime = 0f;
    private bool timerRunning = false;



    [Header("CurText Animation")]
    //floating
    public float curFloatSpeed = 2f;
    public float curFloatAmplitude = 5f;

    private TMP_Text curFloatingTextComponent;
    private TMP_TextInfo curTextInfo;

    //transition
    private Vector3[][] originalVertices;
    private float transitionStartTime;
    public float transitionDuration = 1f;
    public Vector3 offsetToFadein = new Vector3(10, 20, 0);

    [Header("Delete Animation Settings")]
    public float deleteDelayBetweenLetters = 0.05f; // Delay between each letter deletion

    [Header("cursor")]
    public Texture2D customCursor;
    public Vector2 hotspot = Vector2.zero;

 

    //add public var above
    //keyboard key hidden in inspector
    [HideInInspector]
    public GameObject k1, k2, k3, k4, k5, k6, k7, k8, k9, k0, q,w,e,r,t,y,u,i,o,p,a,s,d,f,g,h,j,k,l,z,x,c,v,b,n,m,backspace,enter;
    private Dictionary<KeyCode, GameObject> keyMap = new Dictionary<KeyCode, GameObject>();


    private struct Step
    {
        public KeyCode key;
        public StepAction action;
        public string letter;
    }

    private List<Step> steps = new List<Step>();
    private int currentStep = 0;
    private int currentPhase = 0;
    private Dictionary<string, int> letterOccurrences = new Dictionary<string, int>();
    private bool gameOver = false;
    private bool gameWon = false;
    private bool allPhasesDone = false;
    private bool waitingForNextPhase = false;

    KeyCode[] allowedKeyCodes = {
        KeyCode.A, KeyCode.B, KeyCode.C, KeyCode.D, KeyCode.E, KeyCode.F,
        KeyCode.G, KeyCode.H, KeyCode.I, KeyCode.J, KeyCode.K, KeyCode.L,
        KeyCode.M, KeyCode.N, KeyCode.O, KeyCode.P, KeyCode.Q, KeyCode.R,
        KeyCode.S, KeyCode.T, KeyCode.U, KeyCode.V, KeyCode.W, KeyCode.X,
        KeyCode.Y, KeyCode.Z,
        KeyCode.Alpha0, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3,
        KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7,
        KeyCode.Alpha8, KeyCode.Alpha9
    };
    private bool completeSoundPlayed;


    void Start()
    {
        currentPhase = 0;
        allPhasesDone = false;
        waitingForNextPhase = false;
        showActionPrompt = true;
        curFloatingTextComponent = targetTextUI; //for tmp text animation
        keyMap = new Dictionary<KeyCode, GameObject>()
        {
            { KeyCode.A, a}, { KeyCode.B, b }, { KeyCode.C, c }, { KeyCode.D, d },
            { KeyCode.E, e }, { KeyCode.F, f }, { KeyCode.G, g }, { KeyCode.H, h },
            { KeyCode.I, i }, { KeyCode.J, j }, { KeyCode.K, k }, { KeyCode.L, l },
            { KeyCode.M, m }, { KeyCode.N, n }, { KeyCode.O, o }, { KeyCode.P, p },
            { KeyCode.Q, q }, { KeyCode.R, r }, { KeyCode.S, s }, { KeyCode.T, t },
            { KeyCode.U, u }, { KeyCode.V, v }, { KeyCode.W, w }, { KeyCode.X, x },
            { KeyCode.Y, y }, { KeyCode.Z, z },

            { KeyCode.Alpha0, k0 }, { KeyCode.Alpha1, k1 }, { KeyCode.Alpha2, k2 },
            { KeyCode.Alpha3, k3 }, { KeyCode.Alpha4, k4 }, { KeyCode.Alpha5, k5 },
            { KeyCode.Alpha6, k6 }, { KeyCode.Alpha7, k7 }, { KeyCode.Alpha8, k8 },
            { KeyCode.Alpha9, k9 },

            { KeyCode.Backspace, backspace }, { KeyCode.Return, enter }
        };
        Cursor.SetCursor(customCursor, hotspot, CursorMode.Auto);

        KeyboardShake.Instance.SetShaking(false);

        StartPhase();

        ShowPhases();
    }




    //--- scroll view ---
    #region Scroll View
    void ShowPhases()
    {
        // Clear old buttons
        foreach (Transform child in scrollContent)
        {
            Destroy(child.gameObject);
        }



        for (int i = 0; i < targetTexts.Count; i++)
        {
            int index = i;
            GameObject btnObj = Instantiate(phaseButtonPrefab, scrollContent);
            btnObj.GetComponentInChildren<TextMeshProUGUI>().text = $"{index + 1}. {targetTexts[i]}";

            Button btn = btnObj.GetComponent<Button>();
            btn.onClick.AddListener(() =>
            {
                selectedPhaseIndex = index;
                HighlightSelectedButton(btnObj);
            });

        }


        Canvas.ForceUpdateCanvases();
    }

    void HighlightSelectedButton(GameObject selected)
    {
        foreach (Transform child in scrollContent)
        {
            Image img = child.GetComponent<Image>();
            if (img != null)
                img.color = (child.gameObject == selected) ? phaseSelected : phaseUnselected;
        }
    }

    public void SwapPhase()
    {
        if (selectedPhaseIndex >= 0 && selectedPhaseIndex < targetTexts.Count)
        {
            currentPhase = selectedPhaseIndex;
            RestartCurrentPhase();
        }
    }

    public void DeletePhase()
    {
        if (selectedPhaseIndex >= 0 && selectedPhaseIndex < targetTexts.Count)
        {
            targetTexts.RemoveAt(selectedPhaseIndex);
            if (currentPhase >= targetTexts.Count)
                currentPhase = targetTexts.Count - 1;
            ShowPhases();
            StartPhase();
        }
    }

    public void AddPhase()
    {
        string newPhaseText = inputField.text.Trim();
        if (!string.IsNullOrEmpty(newPhaseText))
        {
            targetTexts.Insert(0,newPhaseText);
            inputField.text = string.Empty;
            ShowPhases();
            EventSystem.current.SetSelectedGameObject(null); 
            //StartPhase();
        }
    }

    public void MovePhaseUp()
    {
        if (selectedPhaseIndex > 0 && selectedPhaseIndex < targetTexts.Count)
        {
            string temp = targetTexts[selectedPhaseIndex];
            targetTexts[selectedPhaseIndex] = targetTexts[selectedPhaseIndex - 1];
            targetTexts[selectedPhaseIndex - 1] = temp;
            selectedPhaseIndex--;
            ShowPhases();
            //StartPhase();
        }
    }

    public void MovePhaseDown()
    {
        if (selectedPhaseIndex >= 0 && selectedPhaseIndex < targetTexts.Count - 1)
        {
            string temp = targetTexts[selectedPhaseIndex];
            targetTexts[selectedPhaseIndex] = targetTexts[selectedPhaseIndex + 1];
            targetTexts[selectedPhaseIndex + 1] = temp;
            selectedPhaseIndex++;
            ShowPhases();
            //StartPhase();
        }
    }
    #endregion


    void Update()
    {
        // tutorial prompt
        if (doneFirstPhase) { showActionPrompt = false; }

        // input field color change
        //InputFieldColorAndBlockInput();

        if (EventSystem.current.currentSelectedGameObject == inputField.gameObject)
        {
            inputField.GetComponent<Image>().color = inputFieldActiveColor;
            return;
        }
        else
        {
            inputField.GetComponent<Image>().color = inputFieldInactiveColor;
        }


        // --- Core Game Logic - Input Handling ---
        #region Core Game Logic

        // Allow Backspace to reset game at any time
        if (Input.GetKeyDown(KeyCode.Backspace))
        {
            //ResetGame();
            RestartCurrentPhase();
            //color
            StartCoroutine(BackspaceColor());
            return;
        }


        // If waiting for next phase, only detect Return to proceed
        if (waitingForNextPhase)
        {
            notMatchedTextUI.text = "Phase complete! Hit Return to continue...";
            if (!doneFirstPhase) { doneFirstPhase = true; }

            if (!completeSoundPlayed)
            {
                audioKeys.ResetPitch();
                audioResult.PlaySound(audioResult.complete);

                completeSoundPlayed = true;
            }

            if (Input.GetKeyDown(KeyCode.Return))
            {

                waitingForNextPhase = false;
                NextPhase();
            }
            return;
        }


        // Block input during win/game over states
        if (gameOver || waitingForNextPhase || allPhasesDone || steps.Count == 0 || currentStep >= steps.Count)
            return;


        // Only process on key events
        foreach (KeyCode kc in allowedKeyCodes)
        {
            if (Input.GetKeyDown(kc) || Input.GetKeyUp(kc))
            {
                if (!timerRunning)
                {
                    timerRunning = true;
                    phaseStartTime = Time.time;
                }

                ProcessStep(kc);
                UpdateText();
                break;
            }
        }

        #endregion


        // blinking cursor
        blinkTimer += Time.deltaTime;
        if (blinkTimer >= cursorBlinkInterval)
        {
            blinkTimer = 0f;
            showCursor = !showCursor;
            UpdateText();
        }

        //timer update
        if (timerRunning)
        {
            currentPhaseDuration = Time.time - phaseStartTime;
            UpdateTimerUI();
        }

    }



    // --- Core game logic ---
    #region Core Game Logic
    void StartPhase()
    {
        if (currentPhase < targetTexts.Count)
        {
            targetText = targetTexts[currentPhase];
            ParseSteps();
            matchedText = string.Empty;
            notMatchedText = string.Empty;
            gameOver = false;
            gameWon = false;
            waitingForNextPhase = false;
            currentStep = 0;
            UpdateText();
        }
        else
        {
            allPhasesDone = true;
            targetTextUI.text = "";
            matchedTextUI.text = "";
            notMatchedTextUI.text = "Congratulations! You completed all phases!";

        }
    }

    void ParseSteps()
    {
        steps.Clear();
        letterOccurrences.Clear();
        foreach (char c in targetText)
        {
            string letter = c.ToString().ToUpper();
            if (!char.IsLetterOrDigit(c))
                continue;

            if (!letterOccurrences.ContainsKey(letter))
                letterOccurrences[letter] = 0;
            letterOccurrences[letter]++;

            StepAction action = (letterOccurrences[letter] % 2 == 1) ? StepAction.Hold : StepAction.Release;

            if (Enum.TryParse(letter, out KeyCode key))
            {
                steps.Add(new Step { key = key, action = action, letter = letter });
            }
        }
    }

    void ProcessStep(KeyCode inputKey)
    {
  
        Step step = steps[currentStep];

        // Check for correct key and action
        if (step.action == StepAction.Hold)
        {
            //correct key hold check

            if (inputKey == step.key && Input.GetKey(step.key))
            {
                matchedText += step.letter;
                currentStep++;
                notMatchedText = string.Empty;


                //effect

                audioKeys.StopAudio();
                audioKeys.AddPitch(.2f);
                audioKeys.PlaySound(audioKeys.pressed);

                CameraShakeAndZoom.Instance.MildShake();
                CameraShakeAndZoom.Instance.OverZoomCam();

                KeyboardShake.Instance.SetShaking(true);
                KeyboardShake.Instance.UpMagnitude();

                SetKeyColor(step.key, keyHoldColor);



                if (currentStep >= steps.Count)
                {
                    gameWon = true;
                    waitingForNextPhase = true;
                }
            }
            else
            {
                //fail 
                gameOver = true;
                notMatchedText = $"Expected to hold '{step.letter}', but got '{inputKey}'. You Lose! Press Backspace to start again";

                audioKeys.StopAudio();
                audioKeys.ResetPitch();
                audioResult.StopAudio();
                audioResult.PlaySound(audioResult.fail);
                //Debug.Log(notMatchedText);

                CameraShakeAndZoom.Instance.StrongShake();

            }
        }
        else // correct key release
        {
            if (inputKey == step.key && !Input.GetKey(step.key))
            {
                matchedText += step.letter;

                //release audio 
                audioKeys.PlaySound(audioKeys.released);
                SetKeyColor(step.key, keyReleaseColor);

                //effect
                CameraShakeAndZoom.Instance.MildShake();
                KeyboardShake.Instance.DownMagnitude();

                currentStep++;
                notMatchedText = string.Empty;
                if (currentStep >= steps.Count)
                {
                    gameWon = true;
                    waitingForNextPhase = true;
                }
            }
            else
            {
                //fail 
                gameOver = true;
                notMatchedText = $"Expected to release '{step.letter}', but got '{inputKey}'. You Lose! Press Backspace to start again";


                audioKeys.StopAudio();
                audioKeys.ResetPitch();
                audioResult.StopAudio();
                audioResult.PlaySound(audioResult.fail);

                //Debug.Log(notMatchedText);

                CameraShakeAndZoom.Instance.StrongShake();
            }
        }
    }

    // Call this after phase win to move to next phase
    public void NextPhase()
    {
        currentPhase++;

        if (timerRunning)
        {
            totalElapsedTime += currentPhaseDuration;
            timerRunning = false;
            currentPhaseDuration = 0f;
        }

        StartPhase();
        completeSoundPlayed = false;

        ResetCommonState();

        // enter key delay color
        StartCoroutine(EnterColor());
    }

    void UpdateText()
    {
        // Build matchedText to match targetText's format and casing
        int revealedCount = currentStep;
        string displayMatched = "";
        int stepIndex = 0;

        foreach (char c in targetText)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (stepIndex < revealedCount)
                {
                    displayMatched += c; // reveal correct letter in original format
                }
                else if (stepIndex == revealedCount)
                {
                    displayMatched += showCursor ? blockChar : " "; // blinking cursor
                }
                else
                {
                    displayMatched += "_"; // future letters
                }

                stepIndex++;
            }
            else
            {
                displayMatched += c; // preserve spaces and punctuation
            }
        }

        targetTextUI.text = targetText;
        matchedTextUI.text = displayMatched;

        // Show action prompt if enabled and game is not over/won
        if (showActionPrompt && !gameOver && !gameWon && currentStep < steps.Count)
        {
            Step nextStep = steps[currentStep];
            string actionText = nextStep.action == StepAction.Hold ? "Hold" : "Release";
            notMatchedTextUI.text = $"{actionText} '{targetText[GetTargetTextIndex(currentStep)]}'";
        }
        else
        {
            notMatchedTextUI.text = notMatchedText;
        }
    }


    // Helper to get the index in targetText for the current step
    int GetTargetTextIndex(int stepIdx)
    {
        int count = 0;
        for (int i = 0; i < targetText.Length; i++)
        {
            if (char.IsLetterOrDigit(targetText[i]))
            {
                if (count == stepIdx)
                    return i;
                count++;
            }
        }
        return targetText.Length - 1;
    }
    #endregion


    // --- Core game logic - Resets ---
    #region Resets
    private void ResetCommonState()
    {
        notMatchedText = string.Empty;
        gameOver = false;
        gameWon = false;
        currentStep = 0;
        waitingForNextPhase = false;

        ParseSteps();

        //*** Handled in coroutine DeleteTextAnim() ***
        //matchedText = string.Empty;
        //UpdateText();

        ResetKeyColors();
        audioKeys.SetVolume(1.0f);
        audioKeys.ResetPitch();

        timerRunning = false;
        currentPhaseDuration = 0f;
        UpdateTimerUI();

        KeyboardShake.Instance.SetShaking(false);
        KeyboardShake.Instance.ResetMagnitude();

        CameraShakeAndZoom.Instance.ResetFOV();

        // Start the delete animation before resetting
        StartCoroutine(DeleteTextAnim());
    }

    private void RestartCurrentPhase()
    {
        targetText = targetTexts[currentPhase];

        ResetCommonState();
    }

    public void ResetGame()
    {
        allPhasesDone = false;
        currentPhase = 0;
        targetText = targetTexts[currentPhase];
        StartPhase();

        ResetCommonState();

        // Reset phase-level timer separately
        totalElapsedTime = 0f;
        UpdateTimerUI();
    }

    private IEnumerator DeleteTextAnim()
    {
        // Get current displayed text with all formatting
        string currentDisplayText = matchedTextUI.text;

        // Extract just the visible characters (no markup)
        string visibleText = GetVisibleCharacters(currentDisplayText);

        // If empty, skip animation
        if (string.IsNullOrEmpty(visibleText))
        {
            matchedText = string.Empty;
            UpdateText();
            yield break;
        }

        // Play initial delete sound
        audioKeys.PlaySound(audioKeys.released);

        // Convert to char array for manipulation
        char[] textChars = visibleText.ToCharArray();

        // Delete from right to left
        for (int i = textChars.Length - 1; i >= 0; i--)
        {
            // Skip underscores, and spaces
            if (textChars[i] == '_' || textChars[i] == ' ')
                continue;

            // Replace this character with underscore (only non-space characters)
            textChars[i] = '_';

            // Rebuild the text with original formatting
            string newText = RebuildWithOriginalFormatting(currentDisplayText, new string(textChars));

            // Update the display
            matchedTextUI.text = newText;
            matchedTextUI.ForceMeshUpdate();

            // Play sound (except for last deletion)
            if (i > 0)
                audioKeys.PlaySound(audioKeys.released);

            yield return new WaitForSeconds(deleteDelayBetweenLetters);
        }

        // Final cleanup
        matchedText = string.Empty;
        UpdateText();
    }

    private string RebuildWithOriginalFormatting(string originalText, string newContent)
    {
        // This preserves the original formatting while replacing content
        // Handles cases like "No <size=75%>F</size>ood" -> "No <size=75%>_</size>___"

        // First get all the formatting tags and their positions
        var tags = new List<(int pos, string tag)>();
        var matches = Regex.Matches(originalText, "<.*?>");
        foreach (Match match in matches)
        {
            tags.Add((match.Index, match.Value));
        }

        // Build new text preserving formatting
        StringBuilder result = new StringBuilder();
        int contentIndex = 0;

        for (int i = 0; i < originalText.Length; i++)
        {
            // Check if current position is a tag
            var currentTag = tags.Find(t => t.pos == i);
            if (currentTag.tag != null)
            {
                result.Append(currentTag.tag);
                i += currentTag.tag.Length - 1; // Skip past tag
            }
            else if (contentIndex < newContent.Length)
            {
                // Insert the new content character
                char c = newContent[contentIndex++];

                // Preserve original spaces exactly
                if (c == '_' && originalText[i] == ' ')
                {
                    result.Append(' '); // Keep original space
                }
                else
                {
                    result.Append(c);
                }
            }
        }

        return result.ToString();
    }

    private string GetVisibleCharacters(string richText)
    {
        // Remove all rich text tags to get just the visible characters
        return Regex.Replace(richText, "<.*?>", string.Empty);
    }



    #endregion


    // input field color change
    void InputFieldColorAndBlockInput()
    {
        if (EventSystem.current.currentSelectedGameObject == inputField.gameObject)
        {
            inputField.GetComponent<Image>().color = inputFieldActiveColor;
            return;
        }
        else
        {
            inputField.GetComponent<Image>().color = inputFieldInactiveColor;
        }
    }


    // Update the timer UI
    void UpdateTimerUI()
    {
        // current phase time
        TimeSpan phaseTime = TimeSpan.FromSeconds(currentPhaseDuration);
        curPhaseTime.text = $"{phaseTime.Hours:D2}\"{phaseTime.Minutes:D2}\'{phaseTime.Seconds:D2}.{phaseTime.Milliseconds / 10:D2}";

        // total time
        TimeSpan totalTime = TimeSpan.FromSeconds(totalElapsedTime);
        totalTime += phaseTime;
        totalTime = TimeSpan.FromSeconds(totalElapsedTime + currentPhaseDuration);
        this.totalTime.text = $"{totalTime.Hours:D2}\"{totalTime.Minutes:D2}\'{totalTime.Seconds:D2}.{totalTime.Milliseconds / 10:D2}";
    }


    // --- keyboard colors ---
    #region Keyboard Colors
    void SetKeyColor(KeyCode key, Color color)
    {
        if (keyMap.TryGetValue(key, out GameObject keyObj))
        {
            Image image = keyObj.GetComponent<Image>();
            if (image != null)
            {
                image.color = color;
            }
        }
    }

    void ResetKeyColors()
    {
        foreach (var kvp in keyMap)
        {
            Image image = kvp.Value.GetComponent<Image>();
            if (image != null)
            {
                image.color = keyReleaseColor; // or a default color
            }
        }
    }

    IEnumerator BackspaceColor()
    {
        SetKeyColor(KeyCode.Backspace, keyHoldColor);
        yield return new WaitForSeconds(0.15f);
        SetKeyColor(KeyCode.Backspace, keyReleaseColor);
        //Debug.Log("Backspace color reset");
    }

    IEnumerator EnterColor()
    {
        SetKeyColor(KeyCode.Return, keyHoldColor);
        yield return new WaitForSeconds(0.15f);
        SetKeyColor(KeyCode.Return, keyReleaseColor);
        
        //Debug.Log("Backspace color reset");
    }
    #endregion


    public void CloseGame()
    {
        Application.Quit();
    }
}

