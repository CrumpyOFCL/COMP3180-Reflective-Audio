using FMOD.Studio;
using FMODUnity;
using UnityEngine;

// A radio in the world. Holds a looping FMOD instance positioned at this object,
// and tells it which era it is in so the same broadcast can be clear in the Past,
// broken up in the Present and dead in the Future.
//
// The instance is released in OnDestroy. Without that, every section unload
// leaves a radio playing somewhere with nothing left to stop it.
public class RadioSource : MonoBehaviour, ITimeSwitchObserver
{
    [Header("Event")]
    [SerializeField] private EventReference radioEvent;

    [Tooltip("Start playing as soon as the section loads.")]
    [SerializeField] private bool playOnStart = true;

    [Header("Era")]
    [Tooltip("Set a labelled Era parameter on the event. Labels must be Past, Present and Future.")]
    [SerializeField] private bool driveEraParameter = true;

    [SerializeField] private string eraParameterName = "Era";

    [Tooltip("Stop and restart the event on every era change, so each era's track " +
             "begins from the top. Leave off to keep one playhead and let the " +
             "parameter crossfade, which suits filtering the same broadcast.")]
    [SerializeField] private bool restartOnEraChange = false;

    [Header("Station")]
    [Tooltip("Optional labelled parameter for switching station. Leave empty if unused.")]
    [SerializeField] private string stationParameterName = "";

    [SerializeField] private string startingStation = "";

    private EventInstance instance;
    private bool isPlaying;


    private void Start()
    {
        if (driveEraParameter)
        {
            TimeSingleton.Instance.AddObserver(this);
        }

        if (playOnStart)
            TurnOn();
    }


    private void OnDestroy()
    {
        if (TimeSingleton.Instance != null)
            TimeSingleton.Instance.RemoveObserver(this);

        Release();
    }


    public void TurnOn()
    {
        if (isPlaying) return;

        if (radioEvent.IsNull)
        {
            Debug.LogWarning($"{name}: no FMOD event assigned, the radio is silent.", this);
            return;
        }

        instance = RuntimeManager.CreateInstance(radioEvent);
        RuntimeManager.AttachInstanceToGameObject(instance, gameObject);

        if (driveEraParameter && TimeSingleton.Instance != null)
            ApplyEra(TimeSingleton.Instance.currentGameState);

        if (!string.IsNullOrEmpty(stationParameterName) && !string.IsNullOrEmpty(startingStation))
            instance.setParameterByNameWithLabel(stationParameterName, startingStation);

        instance.start();
        isPlaying = true;
    }


    // Fades out rather than cutting, so the event's own release envelope plays.
    public void TurnOff()
    {
        if (!isPlaying) return;

        // Qualified: FMODUnity and FMOD.Studio both define STOP_MODE.
        instance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        Release();
    }


    public void Toggle()
    {
        if (isPlaying) TurnOff();
        else TurnOn();
    }


    public void SetStation(string label)
    {
        if (!isPlaying || string.IsNullOrEmpty(stationParameterName)) return;

        instance.setParameterByNameWithLabel(stationParameterName, label);
    }


    public void OnNotify(TimeSingleton.GameState currentTime)
    {
        if (!isPlaying) return;

        if (!restartOnEraChange)
        {
            // One playhead, parameter does the work. The new era's track picks up
            // wherever the old one had reached.
            ApplyEra(currentTime);
            return;
        }

        // Distinct song per era: tear the instance down and build a new one, so
        // the incoming track starts at its beginning rather than mid-phrase.
        instance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        Release();

        TurnOn();
    }


    private void ApplyEra(TimeSingleton.GameState era)
    {
        instance.setParameterByNameWithLabel(eraParameterName, era.ToString());
    }


    private void Release()
    {
        if (instance.isValid())
            instance.release();

        isPlaying = false;
    }
}
