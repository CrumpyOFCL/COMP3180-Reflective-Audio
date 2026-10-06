using UnityEngine;

// RETIRED (FMOD pass) - thin compatibility wrapper. Delete this file once
// nothing references it.
//
// The old MusicManager played two Unity AudioSources (presentMusic/pastMusic)
// in sync and cross-faded them on time switches. EraMusic now does the same job
// in FMOD with one event and a continuous Era parameter (and also covers the
// Future, which MusicManager silently mapped to the Past track). Running both
// would double the music, so this class no longer plays anything:
//
//   * StartMusic() forwards to EraMusic.Instance.Play() (idempotent), so any
//     existing caller or UnityEvent wired to StartMusic keeps working.
//   * It still implements ITimeSwitchObserver so code that passes it around as
//     one keeps compiling, but it no longer registers itself - EraMusic is the
//     observer now.
//   * The old serialized fields are kept so existing scenes load without
//     "missing field" noise; if they are assigned, the legacy AudioSources are
//     stopped and disabled so a leftover clip cannot play on top of FMOD.
//
// Migration: add EraMusic (and EraAmbience) to the persistent Core object,
// remove this component and its two AudioSources, then delete this file.
[DisallowMultipleComponent]
public class MusicManager : MonoBehaviour, ITimeSwitchObserver
{
    [Header("LEGACY - unused, remove after migrating to EraMusic")]
    [SerializeField] private AudioSource presentMusic;
    [SerializeField] private AudioSource pastMusic;
#pragma warning disable 0414 // kept only so existing serialized data still maps
    [SerializeField, Min(0f)] private float fadeDuration = 1f;
#pragma warning restore 0414

    private static bool warned;

    private void Awake()
    {
        SilenceLegacy(presentMusic);
        SilenceLegacy(pastMusic);

        if (!warned)
        {
            warned = true;
            Debug.LogWarning($"{name}: MusicManager is retired. Music now comes from EraMusic " +
                             "(FMOD). Remove this component and its AudioSources.", this);
        }
    }

    private void Start()
    {
        // The old component started its music in Start; keep that contract.
        StartMusic();
    }

    public void StartMusic()
    {
        EraMusic music = EraMusic.Instance;

        if (music == null)
        {
            Debug.LogWarning($"{name}: StartMusic called but there is no EraMusic in the " +
                             "loaded scenes.", this);
            return;
        }

        music.Play();
    }

    // No-op: EraMusic observes TimeSingleton itself.
    public void OnNotify(TimeSingleton.GameState currentTime) { }

    private static void SilenceLegacy(AudioSource source)
    {
        if (source == null) return;

        source.playOnAwake = false;
        source.Stop();
        source.enabled = false;
    }

}
