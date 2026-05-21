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

    /// <summary>
    /// Loads the scene specified by the build index.
    /// </summary>
    public void LoadTargetScene()
    {
        PlaySoundIfAvailable();
        SceneManager.LoadScene(sceneBuildIndex);
    }

    /// <summary>
    /// Quits the application. Works in built builds and logs in the editor.
    /// </summary>
    public void QuitGame()
    {
        PlaySoundIfAvailable();
        
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }

    /// <summary>
    /// Plays the assigned audio clip in 3D space so it survives scene transitions.
    /// </summary>
    private void PlaySoundIfAvailable()
    {
        if (clickSound != null)
        {
            // Plays the sound at the camera's position so it's clearly audible,
            // and won't get cut off when the current scene is destroyed.
            AudioSource.PlayClipAtPoint(clickSound, Camera.main.transform.position);
        }
    }
}