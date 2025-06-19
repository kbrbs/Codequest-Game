using UnityEngine;
using TMPro;
using Firebase.Firestore;
using Firebase.Extensions;
using System;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine.SceneManagement;


public class RegisterManager : MonoBehaviour
{
    public TMP_InputField studentNumberInput;
    public TMP_InputField fullNameInput;
    public TMP_InputField emailInput;
    public TMP_InputField passwordInput;
    public TMP_InputField confirmPasswordInput;
    public TMP_InputField classCodeInput;
    public TextMeshProUGUI feedbackText;

    public string loginSceneName = "LoginScene";

    private FirebaseFirestore db;

    void Start()
    {
        db = FirebaseFirestore.DefaultInstance;
    }

    public void OnRegisterClick()
    {
        string email = emailInput.text;
        string studentNumber = studentNumberInput.text;
        string classCode = classCodeInput.text;
        string fullName = fullNameInput.text;
        string password = passwordInput.text;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(studentNumber) ||
            string.IsNullOrEmpty(classCode) || string.IsNullOrEmpty(fullName) ||
            string.IsNullOrEmpty(password))
        {
            feedbackText.text = "Please fill in all required fields";
            return;
        }

        CheckForDuplicate(email, studentNumber, classCode, async () =>
        {
            string hashedPassword = HashPassword(password);
            await IncrementClassStudentCount(classCode, studentNumber, fullName, email, hashedPassword);
        });
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
            // First query to find the document with matching class code
            Query query = db.Collection("classes").WhereEqualTo("classCode", classCode);
            QuerySnapshot querySnapshot = await query.GetSnapshotAsync();

            if (!querySnapshot.Any())
            {
                feedbackText.text = "Error: Class code not found";
                return;
            }

            // Get the class document
            DocumentReference classRef = querySnapshot.Documents.First().Reference;

            // Check for duplicate email or student number in the students subcollection
            Query studentsQuery = classRef.Collection("students");
            QuerySnapshot studentsSnapshot = await studentsQuery.GetSnapshotAsync();

            bool emailExists = studentsSnapshot.Documents
                .Any(doc => doc.GetValue<string>("email") == email);

            bool studentNumberExists = studentsSnapshot.Documents
                .Any(doc => doc.GetValue<string>("studentNumber") == studentNumber);

            if (emailExists)
            {
                feedbackText.text = "Error: Email already registered in this class";
                return;
            }

            if (studentNumberExists)
            {
                feedbackText.text = "Error: Student number already registered in this class";
                return;
            }

            // If no duplicates found, proceed with registration
            onSuccess?.Invoke();
        }
        catch (Exception ex)
        {
            feedbackText.text = "Error checking for duplicates: " + ex.Message;
        }
    }

    public async Task IncrementClassStudentCount(string classCode, string studentNumber, string fullName, string email, string hashedPassword)
    {
        try
        {
            Query query = db.Collection("classes").WhereEqualTo("classCode", classCode);
            QuerySnapshot querySnapshot = await query.GetSnapshotAsync();

            if (!querySnapshot.Any())
            {
                feedbackText.text = "Error: Class code not found";
                return;
            }

            DocumentReference classRef = querySnapshot.Documents.First().Reference;

            await db.RunTransactionAsync(async transaction =>
            {
                DocumentSnapshot snapshot = await transaction.GetSnapshotAsync(classRef);
                if (snapshot.Exists)
                {
                    Dictionary<string, object> updates = new Dictionary<string, object>
                    {
                        { "studentCount", snapshot.GetValue<int>("studentCount") + 1 }
                    };
                    transaction.Update(classRef, updates);

                    DocumentReference studentRecordRef = classRef.Collection("students").Document();
                    Dictionary<string, object> studentData = new Dictionary<string, object>
                    {
                        { "studentNumber", studentNumber },
                        { "fullName", fullName },
                        { "email", email },
                        { "password", hashedPassword },
                        { "status", "pending" },
                        { "isActive", false },
                        { "createdAt", Timestamp.GetCurrentTimestamp() }
                    };
                    transaction.Set(studentRecordRef, studentData);
                }
            });

            feedbackText.text = "Registration successful. Awaiting approval.";
            LogActivity(email, fullName, classCode);
        }
        catch (Exception ex)
        {
            feedbackText.text = "Error: " + ex.Message;
        }
    }

    void LogActivity(string email, string fullName, string classCode)
    {
        DocumentReference logRef = db.Collection("activity_logs").Document();

        Dictionary<string, object> log = new Dictionary<string, object>
        {
            { "action", "register" },
            { "email", email },
            { "description", $"{fullName} registered to class {classCode}" },
            { "timestamp", Timestamp.GetCurrentTimestamp() }
        };

        logRef.SetAsync(log);
    }
    
        public void LoginButtonClick()
    {
        var sceneLoad = SceneManager.LoadSceneAsync(loginSceneName);
    }
}
