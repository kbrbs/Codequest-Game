using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using System.IO;
using Firebase;
using Firebase.Auth;
using Firebase.Extensions;

public class SplashLoader : MonoBehaviour
{
    public Slider loadingBar;
    public Text toastText;
    public float fakeLoadingTime = 3f;

    private string nextSceneName = "LoginScene";
    private bool firebaseReady = false;

    void Awake()
    {
        DontDestroyOnLoad(gameObject); // Keep Firebase initialized across scenes
    }

    void Start()
    {
        // Initialize Firebase first
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            var dependencyStatus = task.Result;
            if (dependencyStatus == DependencyStatus.Available)
            {
                Debug.Log("✅ Firebase initialized.");
                firebaseReady = true;
            }
            else
            {
                Debug.LogError($"❌ Firebase init failed: {dependencyStatus}");
                firebaseReady = false;
            }

            // Continue to load scene after checking Firebase
            StartCoroutine(LoadWithDelayAndCheck());
        });
    }

    IEnumerator LoadWithDelayAndCheck()
    {
        float elapsed = 0f;

        // Simulate loading bar
        while (elapsed < fakeLoadingTime)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / fakeLoadingTime);
            loadingBar.value = progress;
            yield return null;
        }

        // After loading bar finishes
        DecideNextScene();
        yield return new WaitForSeconds(2f); // allow toast to show
        SceneManager.LoadScene(nextSceneName);
    }

    void DecideNextScene()
    {
        if (Application.internetReachability != NetworkReachability.NotReachable && firebaseReady)
        {
            var user = FirebaseAuth.DefaultInstance.CurrentUser;

            if (user != null)
            {
                ShowToast("✅ Logged in via Firebase.");
                nextSceneName = "HomeScene"; // or your main gameplay scene
            }
            else
            {
                ShowToast("🔐 No session found. Please log in.");
                // nextSceneName = "LoginScene";
                nextSceneName = "RegisterScene";
            }
        }
        else
        {
            if (LocalUserExists())
            {
                ShowToast("📴 Offline mode: Resuming local session.");
                nextSceneName = "HomeScene";
            }
            else
            {
                ShowToast("⚠️ Offline guest mode enabled.");
                nextSceneName = "GuestScene";
            }
        }
    }

    void ShowToast(string message)
    {
        if (toastText != null)
        {
            toastText.text = message;
            toastText.gameObject.SetActive(true);
        }
    }

    bool LocalUserExists()
    {
        // Replace with actual SQLite check logic
        string dbPath = Path.Combine(Application.persistentDataPath, "localSession.db");
        return File.Exists(dbPath);
    }
}
