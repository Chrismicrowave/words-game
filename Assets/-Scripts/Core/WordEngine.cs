using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

public class WordEngine
{
    private readonly List<Step> steps = new List<Step>();
    private readonly Dictionary<string, int> letterOccurrences = new Dictionary<string, int>();
    private string targetText = "";
    private const string CursorBlockChar = "<size=75%>\u2588</size>";

    public int CurrentStep { get; private set; }
    public int TotalSteps => steps.Count;
    public ReadOnlyCollection<Step> Steps => steps.AsReadOnly();
    public string TargetText => targetText;
    public string LastFailureMessage { get; private set; } = "";
    public bool IsComplete => CurrentStep >= TotalSteps;

    public void LoadWord(string word)
    {
        targetText = word;
        CurrentStep = 0;
        LastFailureMessage = "";
        ParseSteps();
    }

    public void Reset()
    {
        CurrentStep = 0;
        LastFailureMessage = "";
    }

    private void ParseSteps()
    {
        steps.Clear();
        letterOccurrences.Clear();

        for (int i = 0; i < targetText.Length; i++)
        {
            char c = targetText[i];
            if (!char.IsLetterOrDigit(c))
                continue;

            string letter = c.ToString().ToUpper();

            if (!letterOccurrences.ContainsKey(letter))
                letterOccurrences[letter] = 0;
            letterOccurrences[letter]++;

            StepAction action = (letterOccurrences[letter] % 2 == 1)
                ? StepAction.Hold
                : StepAction.Release;

            KeyCode key = ParseKeyCode(letter);
            if (key != KeyCode.None)
            {
                steps.Add(new Step
                {
                    Key = key,
                    Action = action,
                    Letter = letter,
                    TargetTextIndex = i
                });
            }
        }
    }

    private KeyCode ParseKeyCode(string letter)
    {
        if (letter.Length == 1 && char.IsDigit(letter[0]))
        {
            return KeyCode.Alpha0 + (letter[0] - '0');
        }

        if (Enum.TryParse(letter, out KeyCode key))
            return key;

        return KeyCode.None;
    }

    public StepResult ProcessInput(KeyCode key, bool isPressed)
    {
        if (CurrentStep >= steps.Count)
            return StepResult.Failed;

        Step step = steps[CurrentStep];
        bool actionMatches = (step.Action == StepAction.Hold && isPressed)
                          || (step.Action == StepAction.Release && !isPressed);

        if (key == step.Key && actionMatches)
        {
            CurrentStep++;
            LastFailureMessage = "";

            if (CurrentStep >= steps.Count)
                return StepResult.PhaseComplete;

            return StepResult.Correct;
        }

        string expectedAction = step.Action == StepAction.Hold ? "hold" : "release";
        LastFailureMessage = $"Expected to {expectedAction} '{step.Letter}', but got '{key}'";
        return StepResult.Failed;
    }

    public string GetDisplayText(bool showCursor)
    {
        string display = "";
        int stepIndex = 0;

        foreach (char c in targetText)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (stepIndex < CurrentStep)
                {
                    display += c;
                }
                else if (stepIndex == CurrentStep)
                {
                    display += showCursor ? CursorBlockChar : " ";
                }
                else
                {
                    display += "_";
                }
                stepIndex++;
            }
            else
            {
                display += c;
            }
        }

        return display;
    }

    public string GetActionPrompt()
    {
        if (CurrentStep >= steps.Count)
            return "";

        Step step = steps[CurrentStep];
        string action = step.Action == StepAction.Hold ? "Hold" : "Release";
        char targetChar = targetText[step.TargetTextIndex];
        return $"{action} '{targetChar}'";
    }
}
