using UnityEngine;
using TMPro;
using Firebase.Firestore;
using Firebase.Extensions;
using System;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;


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

    void IncrementClassStudentCount(string classCode)
    {
        DocumentReference classRef = db.Collection("classes").Document(classCode);

        db.RunTransactionAsync(transaction =>
        {
            return transaction.GetSnapshotAsync(classRef).ContinueWith(task =>
            {
                DocumentSnapshot snapshot = task.Result;
                if (snapshot.Exists)
                {
                    // Only update the studentCount field
                    Dictionary<string, object> updates = new Dictionary<string, object>
                    {
                        { "studentCount", snapshot.GetValue<int>("studentCount") + 1 }
                    };
                    transaction.Update(classRef, updates);
                }
                else
                {
                    // Create new document with only studentCount field
                    transaction.Set(classRef, new Dictionary<string, object> { { "studentCount", 1 } }, 
                        SetOptions.MergeAll);  // Use MergeAll to preserve any existing fields
                }
                return Task.CompletedTask;
            });
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
