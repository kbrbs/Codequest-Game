using Firebase;
using Firebase.Auth;
using Firebase.Firestore;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using System;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;

public class LoginManager : MonoBehaviour
{
    [SerializeField] private TMP_InputField emailInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private TMP_Text feedbackText;
    private FirebaseFirestore db;
    private FirebaseAuth auth;
    public string menuSceneName = "MenuScene";

    async void Start()
    {
        // Initialize Firebase
        await Firebase.FirebaseApp.CheckAndFixDependenciesAsync();

        // Initialize Firestore and Auth after Firebase is ready
        try
        {
            db = FirebaseFirestore.DefaultInstance;
            auth = FirebaseAuth.DefaultInstance;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Firebase initialization error: {ex.Message}");
            feedbackText.text = "Error initializing system";
        }
    }

    public async void OnLoginButtonClick()
    {
        if (db == null)
        {
            feedbackText.text = "System not ready. Please try again.";
            return;
        }

        if (string.IsNullOrEmpty(emailInput.text) || string.IsNullOrEmpty(passwordInput.text))
        {
            feedbackText.text = "Please enter email and password";
            return;
        }


        try
        {
            // Query all classes to find the student
            QuerySnapshot allClassesSnapshot = await db.Collection("classes").GetSnapshotAsync();
            bool studentFound = false;

            foreach (DocumentSnapshot classDoc in allClassesSnapshot.Documents)
            {
                QuerySnapshot studentsSnapshot = await classDoc.Reference
                    .Collection("students")
                    .WhereEqualTo("email", emailInput.text)
                    .GetSnapshotAsync();

                if (studentsSnapshot.Count > 0)
                {
                    DocumentSnapshot studentDoc = studentsSnapshot.Documents.First();
                    string storedPassword = studentDoc.GetValue<string>("password");

                    if (VerifyPassword(passwordInput.text, storedPassword))
                    {
                        studentFound = true;
                        await HandleSuccessfulLogin(studentDoc);
                        break;
                    }
                }
            }

            if (!studentFound)
            {
                feedbackText.text = "Invalid email or password";
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Login error: {ex.Message}");
            feedbackText.text = "Error during login";
        }
    }

    string HashPassword(string password)
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            StringBuilder builder = new StringBuilder();
            foreach (byte b in bytes)
                builder.Append(b.ToString("x2"));
            return builder.ToString();
        }
    }

    void LogActivity(string email, string classCode, string action)
    {
        Dictionary<string, object> log = new Dictionary<string, object>
        {
            { "action", action },
            { "email", email },
            { "description", $"User {email} performed {action} in class {classCode}" },
            { "timestamp", Timestamp.GetCurrentTimestamp() }
        };

        DocumentReference logRef = db.Collection("activity_logs").Document();
        logRef.SetAsync(log);
    }

    private async Task HandleSuccessfulLogin(DocumentSnapshot studentDoc)
    {
        try
        {
            feedbackText.text = "Login successful";

            // Store user data if needed
            // await StoreUserData(studentDoc);  // Uncomment if you need to store user data

            var sceneLoad = SceneManager.LoadSceneAsync("MenuScene");

            while (!sceneLoad.isDone)
            {
                await Task.Yield();
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Login completion error: {ex.Message}");
            feedbackText.text = "Error completing login";
        }
    }

    private bool VerifyPassword(string inputPassword, string storedHash)
    {
        // Implement your hash verification logic here
        // This is a placeholder implementation
        string inputHash = HashPassword(inputPassword);
        return inputHash == storedHash;
    }
}
