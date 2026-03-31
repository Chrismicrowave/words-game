using UnityEngine;

public class MenuAnimOnOff : MonoBehaviour
{
    Animator animator;
    public bool startOn = false;

    void Start()
    {
        animator = GetComponent<Animator>();

        if (animator != null)
        {
            animator.SetBool("ON", startOn);
            animator.SetBool("OFF", !startOn);
        }
    }

    public void Toggle()
    {
        if (animator != null)
        {
            bool isOn = animator.GetBool("ON");
            animator.SetBool("ON", !isOn);
            animator.SetBool("OFF", isOn);
        }
    }

    public void On()
    {
        if (animator != null)
        {
            animator.SetBool("ON", true);
            animator.SetBool("OFF", false);
        }
    }

    public void Off()
    {
        if (animator != null)
        {
            animator.SetBool("ON", false);
            animator.SetBool("OFF", true);
        }
    }


}
