using UnityEngine;
using TMPro;
using Firebase.Firestore;
using Firebase.Auth;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using Newtonsoft.Json;
using UnityEngine.UI;

public class RegisterManager : MonoBehaviour
{
    public TMP_InputField studentNumberInput;
    public TMP_InputField fullNameInput;
    public TMP_InputField emailInput;
    public TMP_InputField classCodeInput;
    public TextMeshProUGUI feedbackText;
    [SerializeField] public Button regButton;
    public Animator buttonAnimator;

    public string loginSceneName = "LoginScene";

    private FirebaseFirestore db;

    void Start()
    {
        db = FirebaseFirestore.DefaultInstance;
    }

    public void OnRegisterClick()
    {
        string email = emailInput.text.Trim();
        string studentNumber = studentNumberInput.text.Trim();
        string classCode = classCodeInput.text.Trim();
        string fullName = fullNameInput.text.Trim();

        Debug.Log("=== Reg Button Clicked ===");

        // Disable reg button immediately
        buttonAnimator.SetTrigger("Clicked");
        SetRegButtonState(false);
        try
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(studentNumber) ||
                string.IsNullOrEmpty(classCode) || string.IsNullOrEmpty(fullName))
            {
                feedbackText.text = "Please fill in all required fields";
                SetRegButtonState(true);
                return;
            }

            CheckForDuplicate(email, studentNumber, classCode, async () =>
            {
                // Check Firebase Auth for existing email
                var auth = FirebaseAuth.DefaultInstance;
                try
                {
                    var fetchProvidersTask = auth.FetchProvidersForEmailAsync(email);
                    await fetchProvidersTask;
                    if (fetchProvidersTask.Result != null && fetchProvidersTask.Result.Any())
                    {
                        feedbackText.text = "Email already registered in authentication system.";
                        SetRegButtonState(true); // <-- Enable button on duplicate
                        return;
                    }
                }
                catch (Exception ex)
                {
                    feedbackText.text = "Error checking authentication";
                    Debug.LogError("Error checking authentication: " + ex.Message);
                    SetRegButtonState(true); // <-- Enable button on error
                    return;
                }

                try
                {
                    // Generate and hash temporary password
                    string tempPassword = GenerateTemporaryPassword();
                    string hashedTempPassword = HashPassword(tempPassword);

                    // Send temporary password to email
                    StartCoroutine(SendTemporaryPasswordEmail(email, tempPassword, fullName));

                    await AddStudentToClass(classCode, studentNumber, fullName, email, hashedTempPassword);
                }
                catch (Exception ex)
                {
                    feedbackText.text = "Registration failed. Please try again.";
                    SetRegButtonState(true);
                    Debug.LogError("Registration error: " + ex.Message);
                }
                finally
                {
                    SetRegButtonState(true); // <-- Always enable button at the end
                }
            });

        }
        catch (Exception ex)
        {
            Debug.LogError($"Unexpected error in login process: {ex.Message}");
            feedbackText.text = "Login failed. Please try again.";
            SetRegButtonState(true);
        }
    }
    private void SetRegButtonState(bool enabled)
    {
        if (regButton != null)
        {
            regButton.interactable = enabled;
            Debug.Log($"Register button {(enabled ? "enabled" : "disabled")}");
        }
    }

    private string GenerateTemporaryPassword(int length = 8)
    {
        const string valid = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
        StringBuilder res = new StringBuilder();
        System.Random rnd = new System.Random();
        while (0 < length--)
            res.Append(valid[rnd.Next(valid.Length)]);
        return res.ToString();
    }

    string HashPassword(string password)
    {
        using (SHA256 sha256Hash = SHA256.Create())
        {
            byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(password));
            StringBuilder builder = new StringBuilder();
            foreach (byte b in bytes)
                builder.Append(b.ToString("x2"));
            return builder.ToString();
        }
    }

    public async void CheckForDuplicate(string email, string studentNumber, string classCode, Action onSuccess)
    {
        try
        {
            Query query = db.Collection("classes").WhereEqualTo("classCode", classCode);
            QuerySnapshot querySnapshot = await query.GetSnapshotAsync();

            if (!querySnapshot.Any())
            {
                feedbackText.text = "Class code not found";
                SetRegButtonState(true);
                return;
            }

            DocumentReference classRef = querySnapshot.Documents.First().Reference;

            // Check for duplicate email or student number in the students subcollection
            Query studentsQuery = classRef.Collection("students");
            QuerySnapshot studentsSnapshot = await studentsQuery.GetSnapshotAsync();

            bool emailExists = studentsSnapshot.Documents
                .Any(doc => doc.ContainsField("email") && doc.GetValue<string>("email") == email);

            bool studentNumberExists = studentsSnapshot.Documents
                .Any(doc => doc.ContainsField("studentNumber") && doc.GetValue<string>("studentNumber") == studentNumber);

            if (emailExists)
            {
                feedbackText.text = "Email already registered in this class";
                SetRegButtonState(true);
                return;
            }

            if (studentNumberExists)
            {
                feedbackText.text = "Student number already registered in this class";
                SetRegButtonState(true);
                return;
            }

            // Check all classes for duplicate studentNumber (without using CollectionGroup)
            bool studentNumberExistsInOtherClass = await CheckStudentNumberInAllClasses(studentNumber, classCode);
            if (studentNumberExistsInOtherClass)
            {
                feedbackText.text = "Student number already registered in another class";
                SetRegButtonState(true);
                return;
            }

            onSuccess?.Invoke();
        }
        catch (Exception ex)
        {
            feedbackText.text = "Error checking for duplicates";
            Debug.LogError("Error checking for duplicates: " + ex.Message);
        }
    }

    private async Task<bool> CheckStudentNumberInAllClasses(string studentNumber, string currentClassCode)
    {
        try
        {
            QuerySnapshot allClassesSnapshot = await db.Collection("classes").GetSnapshotAsync();

            foreach (DocumentSnapshot classDoc in allClassesSnapshot.Documents)
            {
                if (classDoc.ContainsField("classCode") &&
                    classDoc.GetValue<string>("classCode") == currentClassCode)
                    continue;

                QuerySnapshot studentsSnapshot = await classDoc.Reference.Collection("students").GetSnapshotAsync();

                bool studentExists = studentsSnapshot.Documents
                    .Any(doc => doc.ContainsField("studentNumber") &&
                               doc.GetValue<string>("studentNumber") == studentNumber);

                if (studentExists)
                    return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError("Error checking student number in all classes: " + ex.Message);
            return false;
        }
    }

    public async Task AddStudentToClass(string classCode, string studentNumber, string fullName, string email, string hashedTempPassword)
    {
        try
        {
            Query query = db.Collection("classes").WhereEqualTo("classCode", classCode);
            QuerySnapshot querySnapshot = await query.GetSnapshotAsync();

            if (!querySnapshot.Any())
            {
                feedbackText.text = "Class code not found";
                SetRegButtonState(true);
                return;
            }

            DocumentReference classRef = querySnapshot.Documents.First().Reference;

            DocumentReference studentRecordRef = classRef.Collection("students").Document(email);
            Dictionary<string, object> studentData = new Dictionary<string, object>
            {
                { "studentNumber", studentNumber },
                { "name", fullName },
                { "email", email },
                { "tempPassword", hashedTempPassword },
                { "status", "pending" },
                { "isActive", false },
                { "firstLogin", true },
                { "createdAt", Timestamp.GetCurrentTimestamp() }
            };
            await studentRecordRef.SetAsync(studentData);

            feedbackText.text = "Registration successful! Check your email for your temporary password.";
            LogActivity(email, fullName, classCode);
        }
        catch (Exception ex)
        {
            feedbackText.text = ex.Message;
        }
    }

    void LogActivity(string email, string fullName, string classCode)
    {
        DocumentReference logRef = db.Collection("activity_logs").Document();

        Dictionary<string, object> log = new Dictionary<string, object>
        {
            { "action", "register" },
            { "classCode", classCode },
            { "regName", fullName },
            { "studentNumber", studentNumberInput.text },
            { "email", email },
            { "description", $"{fullName} registered to class {classCode}" },
            { "timestamp", Timestamp.GetCurrentTimestamp() }
        };

        logRef.SetAsync(log);
    }

    // SendGrid serializable classes for template emails
    [System.Serializable]
    public class SendGridTemplateData
    {
        public string name;
        public string passcode;
        public string app_name;
    }
    [System.Serializable]
    public class SendGridPersonalization
    {
        public SendGridEmail[] to;
        public Dictionary<string, string> dynamic_template_data;
    }

    [System.Serializable]
    public class SendGridEmail
    {
        public string email;
        public string name;
    }

    [System.Serializable]
    public class SendGridTemplatePayload
    {
        public SendGridPersonalization[] personalizations;
        public SendGridEmail from;
        public string template_id;
    }

    // SendGrid integration with template
    public IEnumerator SendTemporaryPasswordEmail(string toEmail, string tempPassword, string fullName = "")
    {
        string apiKey = "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";
        string fromEmail = "barbosakat26@gmail.com";
        string fromName = "JavArise: To the top";
        string templateId = "d-74c5128171eb490c8a8c9e73aa5c364a";

        // Template data for SendGrid dynamic template
        Dictionary<string, string> templateData = new Dictionary<string, string>
        {
            { "passcode", tempPassword },
            { "name", !string.IsNullOrEmpty(fullName) ? fullName : "Student" },
            { "app_name", "JavArise" }
        };

        SendGridTemplatePayload payload = new SendGridTemplatePayload
        {
            personalizations = new SendGridPersonalization[]
            {
                new SendGridPersonalization
                {
                    to = new SendGridEmail[]
                    {
                        new SendGridEmail
                        {
                            email = toEmail,
                            name = !string.IsNullOrEmpty(fullName) ? fullName : "Student"
                        }
                    },
                    dynamic_template_data = templateData
                }
            },
            from = new SendGridEmail
            {
                email = fromEmail,
                name = fromName
            },
            template_id = templateId
        };

        // string jsonPayload = JsonUtility.ToJson(payload, true);
        string jsonPayload = JsonConvert.SerializeObject(payload, Formatting.Indented);

        Debug.Log("SendGrid Template Payload: " + jsonPayload);

        using (var www = new UnityWebRequest("https://api.sendgrid.com/v3/mail/send", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("SendGrid Error: " + www.error);
                Debug.LogError("Response Code: " + www.responseCode);
                Debug.LogError("Response Text: " + www.downloadHandler.text);

                // Fallback: Show temp password in UI (for development/testing)
                feedbackText.text = $"Email service temporarily unavailable. Your temporary password is: {tempPassword}";
                Debug.LogWarning($"TEMP PASSWORD FOR {toEmail}: {tempPassword}");
            }
            else
            {
                Debug.Log("Temporary password sent via SendGrid template.");
                Debug.Log("SendGrid Response: " + www.downloadHandler.text);
                feedbackText.text = "Registration successful! Check your email for your temporary password.";
            }
        }
    }

    public void LoginButtonClick()
    {
        var sceneLoad = SceneManager.LoadSceneAsync(loginSceneName);
    }
}