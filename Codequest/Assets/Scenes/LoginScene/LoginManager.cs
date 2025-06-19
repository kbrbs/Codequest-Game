using Firebase;
using Firebase.Auth;
using Firebase.Extensions;
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
using System.Text.RegularExpressions;

public class LoginManager : MonoBehaviour
{
    [SerializeField] private TMP_InputField emailInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private TMP_Text feedbackText;
    public string menuSceneName = "MenuScene";
    public string registerSceneName = "RegisterScene";

    private FirebaseFirestore db;
    private FirebaseAuth auth;

    async void Start()
    {
        await Firebase.FirebaseApp.CheckAndFixDependenciesAsync();

        try
        {
            db = FirebaseFirestore.DefaultInstance;
            auth = FirebaseAuth.DefaultInstance;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Firebase init error: {ex.Message}");
            feedbackText.text = "Error initializing system.";
        }
    }

    public async void OnLoginButtonClick()
    {
        if (db == null || auth == null)
        {
            feedbackText.text = "System not ready. Please try again.";
            return;
        }

        string email = emailInput.text.Trim();
        string password = passwordInput.text;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            feedbackText.text = "Please fill in all fields.";
            return;
        }

        if (!IsValidEmail(email))
        {
            feedbackText.text = "Invalid email format.";
            return;
        }

        try
        {
            QuerySnapshot allClasses = await db.Collection("classes").GetSnapshotAsync();
            bool studentFound = false;

            foreach (DocumentSnapshot classDoc in allClasses.Documents)
            {
                QuerySnapshot students = await classDoc.Reference
                    .Collection("students")
                    .WhereEqualTo("email", email)
                    .GetSnapshotAsync();

                if (students.Count > 0)
                {
                    DocumentSnapshot studentDoc = students.Documents.First();
                    string storedHash = studentDoc.GetValue<string>("password");

                    if (!VerifyPassword(password, storedHash))
                    {
                        feedbackText.text = "Incorrect password.";
                        return;
                    }

                    // 🔐 Firebase Auth login
                    await SignInWithFirebaseAuth(email, password);

                    // ✅ Set isActive = true and status = "active"
                    await SetStudentActive(studentDoc.Reference);

                    // 🧠 Log login activity
                    LogActivity(email, "login");

                    studentFound = true;
                    await LoadMenuScene();
                    break;
                }
            }

            if (!studentFound)
            {
                feedbackText.text = "No account found with that email.";
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Login error: {ex.Message}");
            feedbackText.text = "Unexpected error during login.";
        }
    }

    private async Task SignInWithFirebaseAuth(string email, string password)
    {
        try
        {
            await auth.CreateUserWithEmailAndPasswordAsync(email, password);
            // await auth.SignInWithEmailAndPasswordAsync(email, password);
            Debug.Log("Firebase Auth SignIn successful.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Firebase Auth SignIn failed: " + ex.Message);
            feedbackText.text = "Authentication failed.";
            throw;
        }
    }

    // ✅ Updates isActive and status to "active"
    private async Task SetStudentActive(DocumentReference studentRef)
    {
        try
        {
            Dictionary<string, object> updateData = new Dictionary<string, object>
            {
                { "isActive", true },
                { "status", "active" }
            };
            await studentRef.UpdateAsync(updateData);
            Debug.Log("Student status and isActive updated to active.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Failed to update isActive/status: " + ex.Message);
        }
    }

    private async Task LoadMenuScene()
    {
        feedbackText.text = "Login successful!";
        var sceneLoad = SceneManager.LoadSceneAsync(menuSceneName);
        while (!sceneLoad.isDone)
        {
            await Task.Yield();
        }
    }

    private void LogActivity(string email, string action)
    {
        Dictionary<string, object> log = new Dictionary<string, object>
        {
            { "action", action },
            { "email", email },
            { "description", $"User {email} performed {action}" },
            { "timestamp", Timestamp.GetCurrentTimestamp() }
        };

        db.Collection("activity_logs").AddAsync(log);
    }

    private bool VerifyPassword(string inputPassword, string storedHash)
    {
        return HashPassword(inputPassword) == storedHash;
    }

    private string HashPassword(string password)
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

    private bool IsValidEmail(string email)
    {
        return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
    }

    public void RegisterButtonClick()
    {
        var sceneLoad = SceneManager.LoadSceneAsync(registerSceneName);
    }
}
