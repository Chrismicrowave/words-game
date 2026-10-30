using UnityEngine;

[CreateAssetMenu(fileName = "GameConfig", menuName = "Words/Game Config")]
public class GameConfig : ScriptableObject
{
    [Header("Build Settings")]
    public bool isDemo = false;

    [Header("Active Profile")]
    public GameConfigProfile activeProfile;
}
