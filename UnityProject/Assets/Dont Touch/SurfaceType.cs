using UnityEngine;

namespace ReflectiveAudio2D
{
    /// <summary>
    /// Tags a collider with an acoustic surface name.
    ///
    /// The name must match a label on the corresponding FMOD labeled parameter
    /// exactly, for example "Stone", "Wood", "Metal", "Grass", "Water".
    /// Used by FootstepAudio for surface selection, and optionally by
    /// AcousticProbe2D to work out what the surrounding room is made of.
    /// </summary>
    [AddComponentMenu("Reflective Audio 2D/Surface Type")]
    public class SurfaceType : MonoBehaviour
    {
        [Tooltip("Must match an FMOD parameter label exactly, including case.")]
        public string SurfaceName = "Stone";

        [Tooltip("How reflective this surface is, 0 = dead, 1 = hard and bright. " +
                 "Read by AcousticProbe2D to estimate room brightness.")]
        [Range(0f, 1f)]
        public float Reflectivity = 0.5f;
    }
}
