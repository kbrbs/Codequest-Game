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
using UnityEngine.UI;

// Custom exception for authentication failures
public class AuthenticationFailedException : Exception
{
    public AuthenticationFailedException(string message) : base(message) { }
}

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
    [SerializeField] private Button loginButton;
    [SerializeField] private Button changePasswordButton;

    public string menuSceneName = "MenuScene";
    public string registerSceneName = "RegisterScene";

    private FirebaseFirestore db;
    private FirebaseAuth auth;
    private DocumentReference currentStudentRef;
    private string currentEmail;
    private string pendingClassCode;

    private async void Start()
    {
        Debug.Log("=== LoginManager Start ===");
        Debug.Log("Initializing Firebase...");

        try
        {
            var dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync();
            if (dependencyStatus == DependencyStatus.Available)
            {
                db = FirebaseFirestore.DefaultInstance;
                auth = FirebaseAuth.DefaultInstance;
                changePasswordPanel.SetActive(false);
                loginPanel.SetActive(true);
                Debug.Log("Firebase initialized successfully.");
                feedbackText.text = "";
            }
            else
            {
                Debug.LogError($"Could not resolve all Firebase dependencies: {dependencyStatus}");
                feedbackText.text = "Error initializing system. Please restart the application.";
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Firebase initialization error: {ex.Message}");
            feedbackText.text = "System initialization failed. Please restart the application.";
        }
    }

    public async void OnLoginButtonClick()
    {
        Debug.Log("=== Login Button Clicked ===");

        SetLoginButtonState(false);

        try
        {
            if (db == null || auth == null)
            {
                Debug.LogError("Firebase not initialized properly.");
                feedbackText.text = "System not ready. Please restart the application.";
                return;
            }

            string email = emailInput.text.Trim().ToLower();
            string password = passwordInput.text;

            Debug.Log($"Login attempt with email: {email}");

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                Debug.LogWarning("Email or password field is empty.");
                feedbackText.text = "Please fill in all fields.";
                return;
            }

            if (!IsValidEmail(email))
            {
                Debug.LogWarning("Invalid email format entered.");
                feedbackText.text = "Please enter a valid email address.";
                return;
            }

            feedbackText.text = "Checking account...";

            // Try to sign in with Auth first
            try
            {
                feedbackText.text = "Logging in...";
                await AttemptRegularLogin(email, password);
                return;
            }
            catch (FirebaseException ex)
            {
                if (ex.ErrorCode == (int)AuthError.UserNotFound)
                {
                    Debug.Log("User not found in Auth, checking students collection...");
                    // Continue to students collection check below
                }
                else
                {
                    Debug.LogError($"Login failed: {ex.Message}");
                    feedbackText.text = "Invalid login credentials. Please try again.";
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Unexpected login error: {ex.Message}");
                feedbackText.text = "Login failed. Please try again.";
                return;
            }

            // If not in Auth, check students collection for first login
            QuerySnapshot classesSnapshot = await db.Collection("classes").GetSnapshotAsync();
            bool studentFound = false;

            foreach (var classDoc in classesSnapshot.Documents)
            {
                var studentDocRef = classDoc.Reference.Collection("students").Document(email);
                var studentDocSnap = await studentDocRef.GetSnapshotAsync();

                if (studentDocSnap.Exists)
                {
                    studentFound = true;
                    var studentData = studentDocSnap.ToDictionary();
                    string classCode = classDoc.GetValue<string>("classCode");

                    bool firstLogin = studentData.ContainsKey("firstLogin") &&
                                      Convert.ToBoolean(studentData["firstLogin"]);

                    if (firstLogin)
                    {
                        // Verify temporary password
                        string storedTempPassword = studentData.ContainsKey("tempPassword") ?
                            studentData["tempPassword"] as string : null;

                        if (string.IsNullOrEmpty(storedTempPassword))
                        {
                            Debug.LogError("No temporary password found for first login account.");
                            feedbackText.text = "Account setup incomplete. Please contact administrator.";
                            return;
                        }

                        if (VerifyPassword(password, storedTempPassword))
                        {
                            Debug.Log("Temporary password verified successfully.");

                            // Store current session data
                            currentStudentRef = studentDocRef;
                            currentEmail = email;
                            pendingClassCode = classCode;

                            // Show change password panel
                            loginPanel.SetActive(false);
                            changePasswordPanel.SetActive(true);
                            changePassFeedbackText.text = "Please create a new password for your account.";

                            // Clear input fields
                            newPasswordInput.text = "";
                            confirmPasswordInput.text = "";

                            return;
                        }
                        else
                        {
                            Debug.LogWarning("Incorrect temporary password entered.");
                            feedbackText.text = "Incorrect temporary password.";
                            return;
                        }
                    }
                    else
                    {
                        // Should not happen: Auth account should exist if firstLogin is false
                        feedbackText.text = "Account already activated. Please use your regular password.";
                        return;
                    }
                }
            }

            if (!studentFound)
            {
                Debug.LogWarning($"No student account found with email: {email}");
                feedbackText.text = "No account found with this email address.";
            }
            // --- NEW LOGIC END ---
        }
        finally
        {
            SetLoginButtonState(true);
        }
    }

    private async Task AttemptRegularLogin(string email, string password)
    {
        Debug.Log("=== Attempting Regular Login ===");

        try
        {
            var userCredential = await auth.SignInWithEmailAndPasswordAsync(email, password);
            Debug.Log($"Regular login successful for user: {userCredential.User.UserId}");

            // Log successful login activity
            // await LogActivity(
            //     "Regular login successful",
            //     "N/A",
            //     email,
            //     "User logged in successfully with existing account.",
            //     userCredential.User.UserId
            // );

            await LoadMenuScene();
        }
        catch (FirebaseException ex)
        {
            Debug.LogWarning($"Regular login failed: {ex.Message}");

            // Check if it's an authentication error
            if (ex.ErrorCode == (int)AuthError.WrongPassword ||
                ex.ErrorCode == (int)AuthError.UserNotFound)
            {
                Debug.Log("Authentication failed - incorrect credentials. Checking for first login...");
                // Return false to indicate we should check for first login
                throw new AuthenticationFailedException("Credentials not found in Auth");
            }
            // else if (ex.ErrorCode == (int)AuthError.TooManyRequests)
            // {
            //     feedbackText.text = "Too many failed attempts. Please try again later.";
            //     throw;
            // }
            else if (ex.ErrorCode == (int)AuthError.UserDisabled)
            {
                feedbackText.text = "This account has been disabled.";
                throw;
            }
            else
            {
                feedbackText.text = "Login failed. Please try again.";
                throw;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Unexpected error during regular login: {ex.Message}");
            feedbackText.text = "Login failed due to unexpected error.";
            throw;
        }
    }

    private async Task AttemptFirstLogin(string email, string password)
    {
        Debug.Log("=== Attempting First Login ===");

        try
        {
            // Search for student document using email as document ID
            QuerySnapshot classesSnapshot = await db.Collection("classes").GetSnapshotAsync();

            foreach (var classDoc in classesSnapshot.Documents)
            {
                Debug.Log($"Checking class: {classDoc.Id}");

                var studentDocRef = classDoc.Reference.Collection("students").Document(email);
                var studentDocSnap = await studentDocRef.GetSnapshotAsync();

                if (studentDocSnap.Exists)
                {
                    Debug.Log($"Student document found in class: {classDoc.Id}");

                    var studentData = studentDocSnap.ToDictionary();
                    string classCode = classDoc.GetValue<string>("classCode");

                    // Check if this is indeed a first login
                    bool firstLogin = studentData.ContainsKey("firstLogin") &&
                                    Convert.ToBoolean(studentData["firstLogin"]);

                    if (!firstLogin)
                    {
                        Debug.LogWarning("Account found but not marked as first login. Account may be already activated.");
                        feedbackText.text = "Account already activated. Please use your regular password.";
                        return;
                    }

                    // Verify temporary password
                    string storedTempPassword = studentData.ContainsKey("tempPassword") ?
                                              studentData["tempPassword"] as string : null;

                    if (string.IsNullOrEmpty(storedTempPassword))
                    {
                        Debug.LogError("No temporary password found for first login account.");
                        feedbackText.text = "Account setup incomplete. Please contact administrator.";
                        return;
                    }

                    if (VerifyPassword(password, storedTempPassword))
                    {
                        Debug.Log("Temporary password verified successfully.");

                        // Store current session data
                        currentStudentRef = studentDocRef;
                        currentEmail = email;
                        pendingClassCode = classCode;

                        // Show change password panel
                        loginPanel.SetActive(false);
                        changePasswordPanel.SetActive(true);
                        changePassFeedbackText.text = "Please create a new password for your account.";

                        // Clear input fields
                        newPasswordInput.text = "";
                        confirmPasswordInput.text = "";

                        return;
                    }
                    else
                    {
                        Debug.LogWarning("Incorrect temporary password entered.");
                        feedbackText.text = "Incorrect temporary password.";
                        return;
                    }
                }
            }

            // No student document found with this email
            Debug.LogWarning($"No student account found with email: {email}");
            feedbackText.text = "No account found with this email address.";
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error during first login attempt: {ex.Message}");
            feedbackText.text = "Failed to verify account. Please try again.";
        }
    }

    public async void OnChangePasswordSubmit()
    {
        Debug.Log("=== Change Password Submit ===");

        // Disable change password button immediately
        SetChangePasswordButtonState(false);

        try
        {
            string newPass = newPasswordInput.text;
            string confirmPass = confirmPasswordInput.text;

            // Validate input
            if (string.IsNullOrEmpty(newPass) || string.IsNullOrEmpty(confirmPass))
            {
                Debug.LogWarning("New password or confirm password field is empty.");
                changePassFeedbackText.text = "Please fill in all fields.";
                return;
            }

            if (newPass != confirmPass)
            {
                Debug.LogWarning("New passwords do not match.");
                changePassFeedbackText.text = "Passwords do not match.";
                return;
            }

            if (newPass.Length < 6)
            {
                Debug.LogWarning("New password too short.");
                changePassFeedbackText.text = "Password must be at least 6 characters long.";
                return;
            }

            changePassFeedbackText.text = "Creating your account...";

            try
            {
                // Step 1: Create Firebase Auth account
                Debug.Log($"Creating Firebase Auth account for email: {currentEmail}");
                var userCredential = await auth.CreateUserWithEmailAndPasswordAsync(currentEmail, newPass);
                string uid = userCredential.User.UserId;
                Debug.Log($"Firebase Auth account created successfully. UID: {uid}");

                // Step 2: Get current student data
                var studentDocSnap = await currentStudentRef.GetSnapshotAsync();
                if (!studentDocSnap.Exists)
                {
                    Debug.LogError("Student document no longer exists during password change.");
                    changePassFeedbackText.text = "Account error. Please contact administrator.";
                    return;
                }

                var studentData = studentDocSnap.ToDictionary();

                // Step 3: Update student data for new document
                studentData["isActive"] = true;
                studentData["uid"] = uid;
                studentData["firstLogin"] = false;
                studentData["status"] = "active";
                studentData["tempPassword"] = FieldValue.Delete;
                studentData["activatedAt"] = Timestamp.GetCurrentTimestamp();

                // Step 4: Create new student document with UID as document ID
                var classRef = currentStudentRef.Parent.Parent;
                var newStudentRef = classRef.Collection("students").Document(uid);

                Debug.Log($"Creating new student document with UID: {uid}");
                await newStudentRef.SetAsync(studentData, SetOptions.MergeAll);

                // Step 5: Delete old student document (with email as ID)
                Debug.Log($"Deleting old student document with email ID: {currentEmail}");
                await currentStudentRef.DeleteAsync();

                // Update reference
                currentStudentRef = newStudentRef;

                // Step 6: Log successful account activation
                await LogActivity(
                    "Account activated",
                    pendingClassCode,
                    currentEmail,
                    "First login completed successfully. Firebase Auth account created.",
                    uid
                );

                Debug.Log("Account activation completed successfully.");
                changePasswordPanel.SetActive(false);
                feedbackText.text = "Account created successfully!";

                // Step 7: Load menu scene
                await LoadMenuScene();
            }
            catch (FirebaseException ex)
            {
                Debug.LogError($"Firebase Auth account creation failed: {ex.Message}");

                // Check specific auth error codes
                if (ex.ErrorCode == (int)AuthError.WeakPassword)
                {
                    changePassFeedbackText.text = "Password is too weak. Please choose a stronger password.";
                }
                else if (ex.ErrorCode == (int)AuthError.EmailAlreadyInUse)
                {
                    changePassFeedbackText.text = "Email is already in use. Please contact administrator.";
                }
                else
                {
                    changePassFeedbackText.text = "Failed to create account. Please try again.";
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Unexpected error during password change: {ex.Message}");
                changePassFeedbackText.text = "Account creation failed. Please try again.";
            }
        }
        finally
        {
            // Re-enable change password button when process is complete
            SetChangePasswordButtonState(true);
        }
    }

    private void SetLoginButtonState(bool enabled)
    {
        if (loginButton != null)
        {
            loginButton.interactable = enabled;
            Debug.Log($"Login button {(enabled ? "enabled" : "disabled")}");
        }
    }

    private void SetChangePasswordButtonState(bool enabled)
    {
        if (changePasswordButton != null)
        {
            changePasswordButton.interactable = enabled;
            Debug.Log($"Change password button {(enabled ? "enabled" : "disabled")}");
        }
    }

    private async Task LogActivity(string action, string classCode, string email, string description, string performedByUid)
    {
        try
        {
            Debug.Log($"Logging activity: {action} for user: {performedByUid}");

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
            Debug.Log("Activity logged successfully.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to log activity: {ex.Message}");
            // Don't throw - logging failure shouldn't break the login flow
        }
    }

    private bool VerifyPassword(string inputPassword, string storedHash)
    {
        try
        {
            string inputHash = HashPassword(inputPassword);
            bool isValid = inputHash == storedHash;
            Debug.Log($"Password verification: {(isValid ? "SUCCESS" : "FAILED")}");
            return isValid;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Password verification error: {ex.Message}");
            return false;
        }
    }

    private string HashPassword(string password)
    {
        try
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
        catch (Exception ex)
        {
            Debug.LogError($"Password hashing error: {ex.Message}");
            throw;
        }
    }

    private bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    private async Task LoadMenuScene()
    {
        try
        {
            Debug.Log("Loading menu scene...");
            feedbackText.text = "Login successful! Loading...";

            var sceneLoad = SceneManager.LoadSceneAsync(menuSceneName);
            while (!sceneLoad.isDone)
            {
                await Task.Yield();
            }

            Debug.Log("Menu scene loaded successfully.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to load menu scene: {ex.Message}");
            feedbackText.text = "Login successful but failed to load menu. Please restart.";
        }
    }

    public void RegisterButtonClick()
    {
        try
        {
            Debug.Log("Register button clicked. Loading register scene...");
            SceneManager.LoadSceneAsync(registerSceneName);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to load register scene: {ex.Message}");
        }
    }
}