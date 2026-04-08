using UnityEngine;

public class DeactiveOnStart : MonoBehaviour
{
    void Start()
    {
        gameObject.SetActive(false);
    }

}
