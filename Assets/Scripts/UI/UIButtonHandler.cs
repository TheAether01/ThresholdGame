using UnityEngine;
using UnityEngine.SceneManagement;

public class UIButtonHandler : MonoBehaviour
{
    [Header("Scene Management")]
    [Tooltip("The build index of the scene you want to load.")]
    [SerializeField] private int sceneBuildIndex;

    [Header("Audio Settings")]
    [Tooltip("The audio clip to play when the button is clicked.")]
    [SerializeField] private AudioClip clickSound;

    private bool isTransitioning = false;

    /// <summary>
    /// Loads the scene specified by the build index.
    /// </summary>
    public void LoadTargetScene()
    {
        if (isTransitioning) return;
        isTransitioning = true;

        if (clickSound != null)
        {
            StartCoroutine(LoadSceneAfterSound());
        }
        else
        {
            SceneManager.LoadScene(sceneBuildIndex);
        }
    }

    private System.Collections.IEnumerator LoadSceneAfterSound()
    {
        PlaySoundIfAvailable();
        // Wait using unscaled time so it works even if the timeScale is paused/zero
        yield return new WaitForSecondsRealtime(clickSound.length);
        SceneManager.LoadScene(sceneBuildIndex);
    }

    /// <summary>
    /// Quits the application. Works in built builds and logs in the editor.
    /// </summary>
    public void QuitGame()
    {
        if (isTransitioning) return;
        isTransitioning = true;

        if (clickSound != null)
        {
            StartCoroutine(QuitAfterSound());
        }
        else
        {
            PerformQuit();
        }
    }

    private System.Collections.IEnumerator QuitAfterSound()
    {
        PlaySoundIfAvailable();
        yield return new WaitForSecondsRealtime(clickSound.length);
        PerformQuit();
    }

    private void PerformQuit()
    {
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }

    /// <summary>
    /// Plays the assigned audio clip in 3D space.
    /// Note: To prevent clip cutoff during scene loads, the scene transition is delayed.
    /// </summary>
    private void PlaySoundIfAvailable()
    {
        if (clickSound != null)
        {
            // Plays the sound at the camera's position so it's clearly audible
            AudioSource.PlayClipAtPoint(clickSound, Camera.main.transform.position);
        }
    }
}