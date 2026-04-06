using UnityEngine;

[CreateAssetMenu(fileName = "GameConfig", menuName = "Words/Game Config")]
public class GameConfig : ScriptableObject
{
    [Header("Build Settings")]
    public bool isDemo = false;
}
