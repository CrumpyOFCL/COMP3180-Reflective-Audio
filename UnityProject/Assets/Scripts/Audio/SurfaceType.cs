using UnityEngine;

// Marks what a floor is made of. The label is handed straight to the FMOD
// "Surface" parameter, so it must match a label defined in FMOD Studio exactly.
//
// Put this on the tilemap or collider the player actually stands on. Separate
// tilemaps per material is the simplest way to vary surface across a room.
public class SurfaceType : MonoBehaviour
{
    [Tooltip("Must match a label on the FMOD Surface parameter, e.g. Wood, Stone, Carpet, Rubble.")]
    [SerializeField] private string surfaceLabel = "Stone";

    public string SurfaceLabel => surfaceLabel;
}