using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class RollerCoasterAudioDirector : MonoBehaviour
{
    private const string ResourceRoot = "RollerCoasterAudio/";

    private static readonly float[] ProgressCues =
    {
        0.02f,
        0.162f,
        0.334f,
        0.541f,
        0.575f,
        0.72f,
        0.884f,
        0.982f
    };

    [Header("Ride References")]
    [SerializeField] private RollerCoasterRide ride;
    [SerializeField] private Transform vehicleRoot;

    [Header("Continuous Layers")]
    [SerializeField] private AudioClip backgroundMusic;
    [SerializeField] private AudioClip railRumbleLoop;
    [SerializeField] private AudioClip minecartChassisRattleLoop;
    [SerializeField] private AudioClip mountainWindLoop;

    [Header("Ritual Accents")]
    [SerializeField] private AudioClip[] drumHits;
    [SerializeField] private AudioClip[] gongHits;

    [Header("Mechanical Accents")]
    [SerializeField] private AudioClip chainRattle;
    [SerializeField] private AudioClip liftRatchetClick;
    [SerializeField] private AudioClip woodCreak;
    [SerializeField] private AudioClip[] metalImpacts;
    [SerializeField] private AudioClip mysticWhoosh;
    [SerializeField] private AudioClip metalRinging;

    [Header("Mix")]
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.3f;
    [SerializeField, Range(0f, 1f)] private float railVolume = 0.52f;
    [SerializeField, Range(0f, 1f)] private float chassisRattleVolume = 0.18f;
    [SerializeField, Range(0f, 1f)] private float windVolume = 0.48f;
    [SerializeField, Range(0f, 1f)] private float oneShotVolume = 0.72f;
    [SerializeField, Min(0.1f)] private float mixResponse = 3.8f;

    private AudioSource musicSource;
    private AudioSource railSource;
    private AudioSource chassisSource;
    private AudioSource windSource;
    private AudioSource mechanicalOneShotSource;
    private AudioSource ritualOneShotSource;
    private AudioSource[] pooledSources;
    private RollerCoasterRide subscribedRide;
    private bool sourcesReady;
    private bool audioPaused;
    private bool applicationFocused = true;
    private bool applicationPaused;
    private float cueTime;
    private bool wasRunning;
    private bool wasFinished;
    private bool launchCuePlayed;
    private bool brakeCuePlayed;
    private int nextProgressCue;
    private float previousProgress;
    private float previousSlope;
    private float nextTrackJointDistance;
    private float nextRatchetTime;
    private float nextChainTime;
    private float nextCreakTime;
    private float nextSummitCueTime;
    private float nextTurnCueTime;
    private Vector3 previousVehicleForward;

    public bool HasCompleteConfiguration =>
        ride != null &&
        vehicleRoot != null &&
        backgroundMusic != null &&
        railRumbleLoop != null &&
        minecartChassisRattleLoop != null &&
        mountainWindLoop != null &&
        HasClips(drumHits, 4) &&
        HasClips(gongHits, 2) &&
        chainRattle != null &&
        liftRatchetClick != null &&
        woodCreak != null &&
        HasClips(metalImpacts, 2) &&
        mysticWhoosh != null &&
        metalRinging != null;

    public void Configure(RollerCoasterRide newRide, Transform newVehicleRoot)
    {
        UnsubscribeFromRide();
        ride = newRide;
        vehicleRoot = newVehicleRoot;
        LoadBundledClips();

        if (Application.isPlaying && isActiveAndEnabled)
        {
            SubscribeToRide();
        }

        if (Application.isPlaying && sourcesReady && isActiveAndEnabled)
        {
            AssignLoopClips();
            ResetCueState();
            UpdateAudioPause();
            StartContinuousLayers();
        }
    }

    private void Awake()
    {
        ResolveReferences();
        LoadBundledClips();
        CreateAudioSources();
        AssignLoopClips();
        ResetCueState();
        StartContinuousLayers();

        if (HasCompleteConfiguration)
        {
            Debug.Log("Roller coaster audio runtime ready: 6 pooled sources, 4 continuous layers, 13 event clips.", this);
        }
        else
        {
            Debug.LogError("Roller coaster audio runtime is missing one or more configured clips.", this);
        }
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToRide();
        UpdateAudioPause();
        if (sourcesReady)
        {
            StartContinuousLayers();
        }
    }

    private void Update()
    {
        if (ride == null)
        {
            ResolveReferences();
            SubscribeToRide();
            if (ride == null)
            {
                return;
            }
        }

        UpdateAudioPause();
        if (audioPaused)
        {
            return;
        }

        cueTime += Time.deltaTime;
        float progress = ride.NormalizedProgress;
        if (progress < previousProgress)
        {
            ResetForNewRun();
        }

        UpdateContinuousMix(Time.deltaTime);
        HandleRideStateChanges();
        HandleProgressCues(progress);
        HandleMotionCues(Time.deltaTime);

        previousProgress = progress;
        previousSlope = ride.CurrentSlope;
        wasRunning = ride.IsRunning;
        wasFinished = ride.IsFinished;
    }

    private void OnDisable()
    {
        UnsubscribeFromRide();
        StopAllCoroutines();
        StopSource(musicSource);
        StopSource(railSource);
        StopSource(chassisSource);
        StopSource(windSource);
        StopSource(mechanicalOneShotSource);
        StopSource(ritualOneShotSource);
        audioPaused = false;
    }

    private void SubscribeToRide()
    {
        if (subscribedRide == ride)
        {
            return;
        }

        UnsubscribeFromRide();
        subscribedRide = ride;
        if (subscribedRide != null)
        {
            subscribedRide.RunStarted += HandleRunStarted;
            subscribedRide.PauseChanged += HandlePauseChanged;
        }
    }

    private void UnsubscribeFromRide()
    {
        if (subscribedRide != null)
        {
            subscribedRide.RunStarted -= HandleRunStarted;
            subscribedRide.PauseChanged -= HandlePauseChanged;
        }
        subscribedRide = null;
    }

    private void HandleRunStarted(RollerCoasterRide startedRide)
    {
        if (startedRide != ride)
        {
            return;
        }

        ResetForNewRun();
        UpdateAudioPause();
    }

    private void ResetForNewRun()
    {
        StopSource(mechanicalOneShotSource);
        StopSource(ritualOneShotSource);
        cueTime = 0f;
        ResetCueState();
        // A new run owns a fresh launch cue; resuming only changes pause state.
        wasRunning = false;
        wasFinished = false;
    }

    private void HandlePauseChanged(RollerCoasterRide changedRide)
    {
        if (changedRide == ride)
        {
            UpdateAudioPause();
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        applicationFocused = hasFocus;
        UpdateAudioPause();
    }

    private void OnApplicationPause(bool isApplicationPaused)
    {
        applicationPaused = isApplicationPaused;
        UpdateAudioPause();
    }

    private void UpdateAudioPause()
    {
        bool shouldPause = !applicationFocused || applicationPaused || (ride != null && ride.IsPaused);
        if (!isActiveAndEnabled || !sourcesReady || audioPaused == shouldPause)
        {
            return;
        }

        audioPaused = shouldPause;
        foreach (AudioSource source in pooledSources)
        {
            if (source == null)
            {
                continue;
            }
            if (audioPaused)
            {
                source.Pause();
            }
            else
            {
                source.UnPause();
            }
        }

        if (!audioPaused)
        {
            // UnPause preserves active playback; this also restores loops if
            // the director was disabled and re-enabled while the ride paused.
            StartContinuousLayers();
        }
    }

    private void ResolveReferences()
    {
        if (ride == null)
        {
            ride = GetComponent<RollerCoasterRide>();
        }

        if (vehicleRoot == null && ride != null)
        {
            vehicleRoot = ride.VehicleRoot;
        }
    }

    private void LoadBundledClips()
    {
        backgroundMusic = LoadIfMissing(backgroundMusic, "Ancient_Mystery_BGM");
        railRumbleLoop = LoadIfMissing(railRumbleLoop, "Rail_Rumble_Loop");
        minecartChassisRattleLoop = LoadIfMissing(minecartChassisRattleLoop, "Chain_Rattle");
        mountainWindLoop = LoadIfMissing(mountainWindLoop, "Mountain_Wind_Loop");
        chainRattle = LoadIfMissing(chainRattle, "Chain_Rattle");
        liftRatchetClick = LoadIfMissing(liftRatchetClick, "Lift_Ratchet_Click");
        woodCreak = LoadIfMissing(woodCreak, "Wood_Creak");
        mysticWhoosh = LoadIfMissing(mysticWhoosh, "Mystic_Whoosh");
        metalRinging = LoadIfMissing(metalRinging, "Metal_Ringing");

        if (drumHits == null || drumHits.Length < 4)
        {
            drumHits = new[]
            {
                Resources.Load<AudioClip>(ResourceRoot + "Drum_Bass"),
                Resources.Load<AudioClip>(ResourceRoot + "Drum_Floor_Tom"),
                Resources.Load<AudioClip>(ResourceRoot + "Drum_Mid_Tom"),
                Resources.Load<AudioClip>(ResourceRoot + "Drum_Flam")
            };
        }

        if (gongHits == null || gongHits.Length < 2)
        {
            gongHits = new[]
            {
                Resources.Load<AudioClip>(ResourceRoot + "Gong_Deep_01"),
                Resources.Load<AudioClip>(ResourceRoot + "Gong_Deep_02")
            };
        }

        if (metalImpacts == null || metalImpacts.Length < 2)
        {
            metalImpacts = new[]
            {
                Resources.Load<AudioClip>(ResourceRoot + "Metal_Impact_Heavy"),
                Resources.Load<AudioClip>(ResourceRoot + "Metal_Impact_Box")
            };
        }
    }

    private void CreateAudioSources()
    {
        if (sourcesReady)
        {
            return;
        }

        musicSource = CreateSource(10);
        railSource = CreateSource(30);
        chassisSource = CreateSource(35);
        windSource = CreateSource(40);
        mechanicalOneShotSource = CreateSource(20);
        ritualOneShotSource = CreateSource(15);
        mechanicalOneShotSource.volume = 1f;
        ritualOneShotSource.volume = 1f;
        pooledSources = new[]
        {
            musicSource, railSource, chassisSource, windSource,
            mechanicalOneShotSource, ritualOneShotSource
        };
        sourcesReady = true;
    }

    private AudioSource CreateSource(int priority)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.priority = priority;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.volume = 0f;
        return source;
    }

    private void AssignLoopClips()
    {
        ConfigureLoop(musicSource, backgroundMusic);
        ConfigureLoop(railSource, railRumbleLoop);
        ConfigureLoop(chassisSource, minecartChassisRattleLoop);
        ConfigureLoop(windSource, mountainWindLoop);
    }

    private static void ConfigureLoop(AudioSource source, AudioClip clip)
    {
        if (source == null)
        {
            return;
        }

        source.clip = clip;
        source.loop = true;
    }

    private void StartContinuousLayers()
    {
        if (audioPaused)
        {
            return;
        }

        StartLoop(musicSource);
        StartLoop(railSource);
        StartLoop(chassisSource);
        StartLoop(windSource);
    }

    private static void StartLoop(AudioSource source)
    {
        if (source != null && source.clip != null && !source.isPlaying)
        {
            source.Play();
        }
    }

    private void UpdateContinuousMix(float deltaTime)
    {
        float speed01 = Mathf.InverseLerp(0f, 27f, ride.CurrentSpeed);
        float downhill = Mathf.Clamp01(-ride.CurrentSlope * 3.5f);
        float uphill = Mathf.Clamp01(ride.CurrentSlope * 4f);
        bool moving = ride.IsRunning && ride.CurrentSpeed > 0.1f;

        float musicTarget = musicVolume * Mathf.Lerp(1f, 0.72f, speed01);
        float railTarget = moving
            ? railVolume * Mathf.SmoothStep(0.14f, 1f, speed01)
            : 0f;
        float chassisTarget = moving
            ? chassisRattleVolume * Mathf.Lerp(0.28f, 1f, speed01)
            : 0f;
        float windTarget = moving
            ? windVolume * Mathf.Lerp(0.08f, 1f, Mathf.Max(speed01, downhill))
            : windVolume * 0.035f;

        float blend = 1f - Mathf.Exp(-mixResponse * Mathf.Max(0f, deltaTime));
        musicSource.volume = Mathf.Lerp(musicSource.volume, musicTarget, blend);
        railSource.volume = Mathf.Lerp(railSource.volume, railTarget, blend);
        chassisSource.volume = Mathf.Lerp(chassisSource.volume, chassisTarget, blend);
        windSource.volume = Mathf.Lerp(windSource.volume, windTarget, blend);

        railSource.pitch = Mathf.Lerp(0.88f, 1.08f, speed01);
        chassisSource.pitch = Mathf.Lerp(0.94f, 1.08f, speed01);
        windSource.pitch = Mathf.Lerp(0.82f, 1.22f, Mathf.Max(speed01, downhill));
    }

    private void HandleRideStateChanges()
    {
        if (ride.IsRunning && !wasRunning && !launchCuePlayed)
        {
            launchCuePlayed = true;
            PlayRandom(mechanicalOneShotSource, metalImpacts, 0.58f, 0.92f, 1.04f);
            PlayRandom(ritualOneShotSource, gongHits, 0.42f, 0.92f, 1.02f);
            StartCoroutine(PlayDrumPattern(0.45f, 3));
        }

        if (ride.IsFinished && !wasFinished)
        {
            PlayOneShot(mechanicalOneShotSource, metalRinging, 0.46f, 0.9f, 1f);
            PlayRandom(ritualOneShotSource, gongHits, 0.55f, 0.88f, 0.98f);
            StartCoroutine(PlayDrumPattern(0.5f, 3));
        }
    }

    private void HandleProgressCues(float progress)
    {
        while (nextProgressCue < ProgressCues.Length && progress >= ProgressCues[nextProgressCue])
        {
            if (previousProgress < ProgressCues[nextProgressCue])
            {
                TriggerProgressCue(nextProgressCue);
            }
            nextProgressCue++;
        }
    }

    private void TriggerProgressCue(int cueIndex)
    {
        switch (cueIndex)
        {
            case 0:
                PlayRandom(mechanicalOneShotSource, metalImpacts, 0.32f, 0.95f, 1.08f);
                break;
            case 1:
                StartCoroutine(PlayDrumPattern(0.34f, 3));
                break;
            case 2:
                PlayOneShot(mechanicalOneShotSource, chainRattle, 0.38f, 0.86f, 0.98f);
                StartCoroutine(PlayDrumPattern(0.42f, 3));
                break;
            case 3:
                PlayRandom(ritualOneShotSource, gongHits, 0.62f, 0.86f, 0.96f);
                StartCoroutine(PlayDrumPattern(0.68f, 4));
                break;
            case 4:
                PlayOneShot(mechanicalOneShotSource, mysticWhoosh, 0.78f, 0.92f, 1.04f);
                StartCoroutine(PlayDrumPattern(0.76f, 4));
                break;
            case 5:
                StartCoroutine(PlayDrumPattern(0.62f, 3));
                break;
            case 6:
                PlayRandom(ritualOneShotSource, gongHits, 0.38f, 0.94f, 1.05f);
                break;
            case 7:
                PlayBrakeCue();
                break;
        }
    }

    private void HandleMotionCues(float deltaTime)
    {
        if (!ride.IsRunning)
        {
            if (vehicleRoot != null)
            {
                previousVehicleForward = vehicleRoot.forward;
            }
            return;
        }

        float now = cueTime;
        float speed = ride.CurrentSpeed;
        float slope = ride.CurrentSlope;
        bool liftHill = slope > 0.045f && speed < 11.5f;

        if (liftHill && now >= nextRatchetTime)
        {
            float speed01 = Mathf.InverseLerp(4f, 11.5f, speed);
            PlayOneShot(mechanicalOneShotSource, liftRatchetClick, 0.16f, 0.9f, 1.12f);
            nextRatchetTime = now + Mathf.Lerp(0.42f, 0.2f, speed01);
        }

        if (liftHill && now >= nextChainTime)
        {
            PlayOneShot(mechanicalOneShotSource, chainRattle, 0.24f, 0.84f, 1.02f);
            nextChainTime = now + Random.Range(1.1f, 1.65f);
        }

        if (liftHill && now >= nextCreakTime)
        {
            PlayOneShot(mechanicalOneShotSource, woodCreak, 0.24f, 0.82f, 0.98f);
            nextCreakTime = now + Random.Range(4.5f, 7.5f);
        }

        int jointSafety = 0;
        while (speed > 3f && ride.CurrentDistance >= nextTrackJointDistance && jointSafety < 3)
        {
            float jointVolume = Mathf.Lerp(0.12f, 0.3f, Mathf.InverseLerp(3f, 27f, speed));
            PlayRandom(mechanicalOneShotSource, metalImpacts, jointVolume, 0.92f, 1.12f);
            nextTrackJointDistance += Random.Range(4.3f, 7.2f);
            jointSafety++;
        }

        if (slope < -0.08f && previousSlope >= -0.08f && speed > 6f)
        {
            PlayOneShot(mechanicalOneShotSource, mysticWhoosh, 0.55f, 0.94f, 1.08f);
            PlayRandom(ritualOneShotSource, drumHits, 0.46f, 0.9f, 1.02f);
        }

        if (previousSlope > 0.055f && slope <= 0.018f && now >= nextSummitCueTime)
        {
            PlayRandom(ritualOneShotSource, gongHits, 0.4f, 0.9f, 1.02f);
            nextSummitCueTime = now + 9f;
        }

        if (vehicleRoot != null && previousVehicleForward.sqrMagnitude > 0.5f && deltaTime > 0.0001f)
        {
            float turnRate = Vector3.Angle(previousVehicleForward, vehicleRoot.forward) / deltaTime;
            if (turnRate > 18f && speed > 9f && now >= nextTurnCueTime)
            {
                AudioClip turnClip = Random.value > 0.48f ? woodCreak : chainRattle;
                PlayOneShot(mechanicalOneShotSource, turnClip, 0.2f, 0.88f, 1.08f);
                nextTurnCueTime = now + Random.Range(3.2f, 5.2f);
            }
        }

        if (vehicleRoot != null)
        {
            previousVehicleForward = vehicleRoot.forward;
        }

        if (!brakeCuePlayed && ride.NormalizedProgress >= 0.982f)
        {
            PlayBrakeCue();
        }
    }

    private void PlayBrakeCue()
    {
        if (brakeCuePlayed)
        {
            return;
        }

        brakeCuePlayed = true;
        PlayRandom(mechanicalOneShotSource, metalImpacts, 0.68f, 0.78f, 0.92f);
        PlayOneShot(mechanicalOneShotSource, metalRinging, 0.42f, 0.82f, 0.94f);
        StartCoroutine(PlayDrumPattern(0.38f, 2));
    }

    private IEnumerator PlayDrumPattern(float intensity, int hitCount)
    {
        for (int index = 0; index < hitCount; index++)
        {
            while (audioPaused)
            {
                yield return null;
            }

            float accent = index == hitCount - 1 ? 1.12f : 1f;
            PlayRandom(
                ritualOneShotSource,
                drumHits,
                intensity * accent,
                0.88f,
                1.04f);

            if (index < hitCount - 1)
            {
                float remaining = index == hitCount - 2 ? 0.46f : 0.31f;
                while (remaining > 0f)
                {
                    yield return null;
                    if (!audioPaused)
                    {
                        remaining -= Time.deltaTime;
                    }
                }
            }
        }
    }

    private void ResetCueState()
    {
        StopAllCoroutines();
        previousProgress = ride == null ? 0f : ride.NormalizedProgress;
        previousSlope = ride == null ? 0f : ride.CurrentSlope;
        wasRunning = ride != null && ride.IsRunning;
        wasFinished = ride != null && ride.IsFinished;
        launchCuePlayed = previousProgress > 0.001f;
        brakeCuePlayed = false;
        nextProgressCue = 0;
        while (nextProgressCue < ProgressCues.Length && ProgressCues[nextProgressCue] <= previousProgress)
        {
            nextProgressCue++;
        }

        float currentDistance = ride == null ? 0f : ride.CurrentDistance;
        nextTrackJointDistance = currentDistance + Random.Range(3.5f, 5.5f);
        nextRatchetTime = cueTime + 0.1f;
        nextChainTime = cueTime + 0.45f;
        nextCreakTime = cueTime + Random.Range(3f, 5f);
        nextSummitCueTime = cueTime + 2f;
        nextTurnCueTime = cueTime + 2f;
        previousVehicleForward = vehicleRoot == null ? Vector3.forward : vehicleRoot.forward;
    }

    private void PlayRandom(
        AudioSource source,
        AudioClip[] clips,
        float volume,
        float minimumPitch,
        float maximumPitch)
    {
        AudioClip clip = PickRandomClip(clips);
        PlayOneShot(source, clip, volume, minimumPitch, maximumPitch);
    }

    private void PlayOneShot(
        AudioSource source,
        AudioClip clip,
        float volume,
        float minimumPitch,
        float maximumPitch)
    {
        if (source == null || clip == null)
        {
            return;
        }

        source.pitch = Random.Range(minimumPitch, maximumPitch);
        source.PlayOneShot(clip, Mathf.Clamp01(volume * oneShotVolume));
    }

    private static AudioClip PickRandomClip(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0)
        {
            return null;
        }

        int start = Random.Range(0, clips.Length);
        for (int offset = 0; offset < clips.Length; offset++)
        {
            AudioClip clip = clips[(start + offset) % clips.Length];
            if (clip != null)
            {
                return clip;
            }
        }

        return null;
    }

    private static AudioClip LoadIfMissing(AudioClip clip, string resourceName)
    {
        return clip != null ? clip : Resources.Load<AudioClip>(ResourceRoot + resourceName);
    }

    private static bool HasClips(AudioClip[] clips, int minimumCount)
    {
        if (clips == null || clips.Length < minimumCount)
        {
            return false;
        }

        for (int index = 0; index < minimumCount; index++)
        {
            if (clips[index] == null)
            {
                return false;
            }
        }

        return true;
    }

    private static void StopSource(AudioSource source)
    {
        if (source != null)
        {
            source.Stop();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureDirectorsExist()
    {
        RollerCoasterRide[] rides = Object.FindObjectsByType<RollerCoasterRide>(
            FindObjectsInactive.Exclude);
        foreach (RollerCoasterRide activeRide in rides)
        {
            RollerCoasterAudioDirector director = activeRide.GetComponent<RollerCoasterAudioDirector>();
            if (director == null)
            {
                director = activeRide.gameObject.AddComponent<RollerCoasterAudioDirector>();
            }

            director.Configure(activeRide, activeRide.VehicleRoot);
        }
    }
}
