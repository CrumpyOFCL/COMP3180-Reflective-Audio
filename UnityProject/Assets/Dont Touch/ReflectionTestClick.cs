using UnityEngine;
using UnityEngine.InputSystem;

public class ReflectionTestClick : MonoBehaviour
{
    [SerializeField] private FMODUnity.EventReference clickEvent;
    [SerializeField] private Key key = Key.T;

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame)
            FMODUnity.RuntimeManager.PlayOneShotAttached(clickEvent, gameObject);
    }
}