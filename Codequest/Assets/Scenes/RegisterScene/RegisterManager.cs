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


public class RegisterManager : MonoBehaviour
{
    public TMP_InputField studentNumberInput;
    public TMP_InputField fullNameInput;
    public TMP_InputField emailInput;
    public TMP_InputField passwordInput;
    public TMP_InputField confirmPasswordInput;
    public TMP_InputField classCodeInput;
    public TextMeshProUGUI feedbackText;

    private FirebaseFirestore db;

    void Start()
    {
        db = FirebaseFirestore.DefaultInstance;
    }

    public void OnRegisterClick()
    {
        string studentNumber = studentNumberInput.text.Trim();
        string fullName = fullNameInput.text.Trim();
        string email = emailInput.text.Trim();
        string password = passwordInput.text;
        string confirmPassword = confirmPasswordInput.text;
        string classCode = classCodeInput.text.Trim();

        if (string.IsNullOrEmpty(studentNumber) || string.IsNullOrEmpty(fullName) ||
            string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password) ||
            string.IsNullOrEmpty(confirmPassword) || string.IsNullOrEmpty(classCode))
        {
            feedbackText.text = "All fields are required.";
            return;
        }

        if (password != confirmPassword)
        {
            feedbackText.text = "Passwords do not match.";
            return;
        }

        CheckForDuplicate(email, studentNumber, () =>
        {
            string hashedPassword = HashPassword(password);
            SaveStudentToFirestore(studentNumber, fullName, email, hashedPassword, classCode);
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

    void CheckForDuplicate(string email, string studentNumber, Action onSuccess)
    {
        db.Collection("students")
            .WhereEqualTo("email", email)
            .GetSnapshotAsync().ContinueWithOnMainThread(emailTask =>
        {
            if (emailTask.Result.Count > 0)
            {
                feedbackText.text = "Email already exists.";
                return;
            }

            db.Collection("students")
                .WhereEqualTo("studentNumber", studentNumber)
                .GetSnapshotAsync().ContinueWithOnMainThread(snTask =>
            {
                if (snTask.Result.Count > 0)
                {
                    feedbackText.text = "Student number already exists.";
                    return;
                }

                // No duplicates found
                onSuccess?.Invoke();
            });
        });
    }

    void SaveStudentToFirestore(string studentNumber, string fullName, string email, string hashedPassword, string classCode)
    {
        DocumentReference studentDoc = db.Collection("students").Document();

        Dictionary<string, object> studentData = new Dictionary<string, object>
        {
            { "studentNumber", studentNumber },
            { "fullName", fullName },
            { "email", email },
            { "password", hashedPassword },
            { "classCode", classCode },
            { "status", "pending" },
            { "isActive", false },
            { "createdAt", Timestamp.GetCurrentTimestamp() }
        };

        studentDoc.SetAsync(studentData).ContinueWithOnMainThread(task =>
        {
            if (task.IsCompletedSuccessfully)
            {
                feedbackText.text = "Registration successful. Awaiting approval.";
                IncrementClassStudentCount(classCode);
                LogActivity(email, fullName, classCode);
            }
            else
            {
                feedbackText.text = "Error: " + task.Exception?.Message;
            }
        });
    }

    public async void IncrementClassStudentCount(string classCode)
    {
        // First query to find the document with matching class code
        Query query = db.Collection("classes").WhereEqualTo("classCode", classCode);
        QuerySnapshot querySnapshot = await query.GetSnapshotAsync();

        if (querySnapshot.Count == 0)
        {
            throw new ArgumentException($"No class found with code: {classCode}");
        }

        // Get the first (and should be only) matching document
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
            }
            else
            {
                transaction.Set(classRef, new Dictionary<string, object> { { "studentCount", 1 } }, 
                    SetOptions.MergeAll);
            }
        });
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
}
