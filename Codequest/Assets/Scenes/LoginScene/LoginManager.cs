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

public class LoginManager : MonoBehaviour
{
    [SerializeField] private TMP_InputField emailInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private TMP_Text feedbackText;
    [SerializeField] private GameObject loginPanel;
    [SerializeField] private GameObject changePasswordPanel;
    [SerializeField] private TMP_InputField newPasswordInput;
    [SerializeField] private TMP_InputField confirmPasswordInput;
    [SerializeField] private TMP_Text changePassFeedbackText;

    public string menuSceneName = "MenuScene";
    public string registerSceneName = "RegisterScene";

    private FirebaseFirestore db;
    private FirebaseAuth auth;
    private DocumentReference currentStudentRef;
    private string currentEmail;
    private string pendingClassCode;

    private async void Start()
    {
        var dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync();
        if (dependencyStatus == DependencyStatus.Available)
        {
            db = FirebaseFirestore.DefaultInstance;
            auth = FirebaseAuth.DefaultInstance;
            changePasswordPanel.SetActive(false);
            loginPanel.SetActive(true);
        }
        else
        {
            Debug.LogError($"Could not resolve all Firebase dependencies: {dependencyStatus}");
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
        string tempPassword = passwordInput.text;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(tempPassword))
        {
            feedbackText.text = "Please fill in all fields.";
            return;
        }

        // 1. Try Auth login first
        try
        {
            var providers = await auth.FetchProvidersForEmailAsync(email);
            if (providers != null && providers.Any())
            {
                // Email exists in Auth, check if password is correct
                if (providers.Contains("password"))
                {
                    try
                    {
                        var userCredential = await auth.SignInWithEmailAndPasswordAsync(email, tempPassword);
                        await UpdateStudentIsActiveByUid(userCredential.User.UserId);
                        await LoadMenuScene();
                        return;
                    }
                    catch (Exception)
                    {
                        feedbackText.text = "Incorrect password.";
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            feedbackText.text = "Auth check error: " + ex.Message;
            return;
        }
        

        // 2. Check students collection by email (doc ID)
        try
        {
            QuerySnapshot classesSnapshot = await db.Collection("classes").GetSnapshotAsync();
            bool found = false;
            foreach (var classDoc in classesSnapshot.Documents)
            {
                var studentDocRef = classDoc.Reference.Collection("students").Document(email);
                var studentDocSnap = await studentDocRef.GetSnapshotAsync();
                if (studentDocSnap.Exists)
                {
                    found = true;
                    currentStudentRef = studentDocRef;
                    currentEmail = email;
                    pendingClassCode = classDoc.GetValue<string>("classCode");

                    var studentData = studentDocSnap.ToDictionary();
                    string storedTempPassword = studentData.ContainsKey("tempPassword") ? studentData["tempPassword"] as string : null;

                    if (VerifyPassword(tempPassword, storedTempPassword))
                    {
                        // Show change password panel
                        loginPanel.SetActive(false);
                        changePasswordPanel.SetActive(true);
                        changePassFeedbackText.text = "";
                        return;
                    }
                    else
                    {
                        feedbackText.text = "Incorrect temporary password.";
                        return;
                    }
                }
            }
            if (!found)
                feedbackText.text = "No account found with that email.";
        }
        catch (Exception ex)
        {
            Debug.LogError($"Login error: {ex.Message}");
            feedbackText.text = "Unexpected error during login.";
        }
    }

    // Called by the submit button on the change password panel
    public async void OnChangePasswordSubmit()
    {
        string newPass = newPasswordInput.text;
        string confirmPass = confirmPasswordInput.text;

        if (string.IsNullOrEmpty(newPass) || string.IsNullOrEmpty(confirmPass))
        {
            changePassFeedbackText.text = "Please fill in all fields.";
            return;
        }
        if (newPass != confirmPass)
        {
            changePassFeedbackText.text = "Passwords do not match.";
            return;
        }
        if (newPass.Length < 6)
        {
            changePassFeedbackText.text = "Password must be at least 6 characters.";
            return;
        }

        // 1. Create Auth account
        try
        {
            Debug.Log($"Creating user with email: {currentEmail}");
            var userCredential = await auth.CreateUserWithEmailAndPasswordAsync(currentEmail, newPass);
            string uid = userCredential.User.UserId;

            // 2. Copy student data to new doc with UID as ID
            var studentDocSnap = await currentStudentRef.GetSnapshotAsync();
            if (!studentDocSnap.Exists)
            {
                changePassFeedbackText.text = "Student record not found.";
                return;
            }
            var studentData = studentDocSnap.ToDictionary();
            studentData["isActive"] = true;
            studentData["uid"] = uid;
            studentData["firstLogin"] = false;
            studentData["status"] = "active";
            studentData["tempPassword"] = FieldValue.Delete;

            // Reference to new doc with UID as ID
            var classRef = currentStudentRef.Parent.Parent;
            var newStudentRef = classRef.Collection("students").Document(uid);

            // Use merge:true so FieldValue.Delete works
            await newStudentRef.SetAsync(studentData, SetOptions.MergeAll);
            await currentStudentRef.DeleteAsync();
            currentStudentRef = newStudentRef;

            // 3. Go to menu
            changePasswordPanel.SetActive(false);
            await LogActivity(
                "First login complete",
                pendingClassCode,
                currentEmail,
                "Firebase Auth account created with new password.",
                userCredential.User.UserId
            );
            feedbackText.text = "Password changed successfully!";
            await LoadMenuScene();
        }
        catch (Exception ex)
        {
            changePassFeedbackText.text = "Failed to create account: " + ex.Message;
            Debug.LogError("Change password error: " + ex);
        }
    }

    private async Task LogActivity(string action, string classCode, string email, string description, string performedByUid)
    {
        var logRef = db.Collection("activity_logs").Document();
        var log = new Dictionary<string, object>
        {
            { "action", action },
            { "classCode", classCode },
            { "performedByEmail", email },
            { "description", description },
            { "timestamp", Timestamp.GetCurrentTimestamp() },
            { "performedBy", performedByUid }
        };
        await logRef.SetAsync(log);
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

    private async Task UpdateStudentIsActiveByUid(string uid)
    {
        QuerySnapshot classesSnapshot = await db.Collection("classes").GetSnapshotAsync();
        foreach (var classDoc in classesSnapshot.Documents)
        {
            var studentDocRef = classDoc.Reference.Collection("students").Document(uid);
            var studentDocSnap = await studentDocRef.GetSnapshotAsync();
            if (studentDocSnap.Exists)
            {
                await studentDocRef.UpdateAsync(new Dictionary<string, object>
                {
                    { "isActive", true }
                });
                break;
            }
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

    public void RegisterButtonClick()
    {
        var sceneLoad = SceneManager.LoadSceneAsync(registerSceneName);
    }
}
