using System;

[Flags]
public enum GameResetTargets
{
    None = 0,
    AudioSettings = 1 << 0,
    DisplaySettings = 1 << 1,
    VisualEffects = 1 << 2,
    InputHints = 1 << 3,
    UiLanguage = 1 << 4,
    Pinyin = 1 << 5,
    PlayerName = 1 << 6,
    PanelVisibility = 1 << 7,
    NavigationMemory = 1 << 8,
    EnglishChallengeProgress = 1 << 9,
    ChineseChallengeProgress = 1 << 10,
    TimeRecords = 1 << 11,
    MyLists = 1 << 12,
    CustomLists = 1 << 13,
    LocalAchievements = 1 << 14,
    AllLocal = (1 << 15) - 1
}
