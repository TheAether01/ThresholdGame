using UnityEngine;

public class BackgroundMusic : MonoBehaviour
{
    private static BackgroundMusic instance;

    [Header("Audio Settings")]
    [Tooltip("Drag your background music audio clip here.")]
    public AudioClip musicClip;

    [Range(0f, 1f)]
    public float volume = 0.5f;

    private AudioSource audioSource;

    void Awake()
    {
        // Singleton pattern: Prevents a second copy of music from spawning 
        // if you return to this scene later.
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        // Keeps the music playing seamlessly when you switch scenes
        DontDestroyOnLoad(gameObject);

        // Set up the AudioSource component programmatically
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.clip = musicClip;
        audioSource.loop = true;
        audioSource.volume = volume;
        audioSource.playOnAwake = true;
    }

    void Start()
    {
        // Play the music if a clip is assigned
        if (musicClip != null)
        {
            audioSource.Play();
        }
        else
        {
            Debug.LogWarning("BackgroundMusic: No audio clip assigned in the Inspector!", this);
        }
    }

    // Dynamic volume adjustment (useful if you hook this up to an options menu slider later)
    public void SetVolume(float newVolume)
    {
        volume = Mathf.Clamp01(newVolume);
        if (audioSource != null)
        {
            audioSource.volume = volume;
        }
    }
}