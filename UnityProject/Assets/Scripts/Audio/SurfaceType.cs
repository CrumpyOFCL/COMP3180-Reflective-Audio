using UnityEngine;

// Marks what a floor is made of. The label is handed straight to the FMOD
// "Surface" parameter, so it must match a label defined in FMOD Studio exactly.
//
// Put this on the tilemap or collider the player actually stands on. Separate
// tilemaps per material is the simplest way to vary surface across a room.
//
// CHANGED (FMOD pass): this is now the ONLY SurfaceType in the project. The
// namespaced ReflectiveAudio2D.SurfaceType (field "SurfaceName") is retired -
// delete that file. Its Reflectivity value lives here now so AcousticProbe2D's
// Brightness sampling keeps working. Any object that carried the old namespaced
// component will show "missing script" and must be re-tagged with this one.
public class SurfaceType : MonoBehaviour
{
    [Tooltip("Must match a label on the FMOD Surface parameter, e.g. Wood, Stone, Carpet, Rubble.")]
    [SerializeField] private string surfaceLabel = "Stone";

    [Tooltip("How reflective this surface is, 0 = dead, 1 = hard and bright. " +
             "Read by AcousticProbe2D to estimate room brightness.")]
    [Range(0f, 1f)]
    [SerializeField] private float reflectivity = 0.5f;

    public string SurfaceLabel => surfaceLabel;

    public float Reflectivity => reflectivity;
}
