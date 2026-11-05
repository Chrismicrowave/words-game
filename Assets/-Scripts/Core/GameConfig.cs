using UnityEngine;

[CreateAssetMenu(fileName = "GameConfig", menuName = "Words/Game Config")]
public class GameConfig : ScriptableObject
{
    [Header("Active Profile")]
    public GameConfigProfile activeProfile;
}
