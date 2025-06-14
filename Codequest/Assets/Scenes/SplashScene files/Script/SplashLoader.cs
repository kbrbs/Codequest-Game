using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class SplashLoader : MonoBehaviour
{
    public Slider loadingBar;
    public string nextSceneName = "LoginScene"; // Change this to your actual next scene
    public float fakeLoadingTime = 3f; // Simulated minimum loading time in seconds

    void Start()
    {
        StartCoroutine(LoadWithDelay());
    }

    IEnumerator LoadWithDelay()
    {
        float elapsed = 0f;

        // Simulate loading progress
        while (elapsed < fakeLoadingTime)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / fakeLoadingTime);
            loadingBar.value = progress;
            yield return null;
        }

        // Then load the next scene
        SceneManager.LoadScene(nextSceneName);
    }
}
