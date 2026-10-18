using UnityEngine;

// SHOWCASE: a plain follow camera for the acoustics test scene, which has none
// of the game's camera rig. Put it on the Main Camera (orthographic).
public class ShowcaseCamera : MonoBehaviour
{
    [Tooltip("Left empty, it finds the Player in the scene.")]
    [SerializeField] private Transform target;
    [SerializeField] private Vector2 offset = new Vector2(0f, 2f);
    [SerializeField] private float smoothTime = 0.15f;

    private Vector3 velocity;

    private void Start()
    {
        if (target == null)
        {
            Player player = FindAnyObjectByType<Player>();
            if (player != null) target = player.transform;
        }

        if (target != null) transform.position = Goal();
    }

    private void LateUpdate()
    {
        if (target == null) return;
        transform.position = Vector3.SmoothDamp(transform.position, Goal(), ref velocity, smoothTime);
    }

    private Vector3 Goal() =>
        new Vector3(target.position.x + offset.x, target.position.y + offset.y, transform.position.z);
}
