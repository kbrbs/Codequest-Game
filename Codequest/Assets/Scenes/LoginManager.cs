using Firebase;
using Firebase.AppCheck;
using Firebase.Database;
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
    public Animator buttonAnimator;

    public Animator buttonChangeAnimator;
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
            // Check dependencies first
            var dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync();
            Debug.Log($"Firebase dependency status: {dependencyStatus}");

            if (dependencyStatus == DependencyStatus.Available)
            {
                // Initialize Firebase services
                FirebaseApp app = FirebaseApp.DefaultInstance;
                auth = FirebaseAuth.GetAuth(app);
                db = FirebaseFirestore.GetInstance(app);

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
            Debug.LogError($"Stack trace: {ex.StackTrace}");
            feedbackText.text = "System initialization failed. Please restart the application.";
        }
    }

    public async void OnLoginButtonClick()
    {
        Debug.Log("=== Login Button Clicked ===");

        // Disable login button to prevent multiple clicks
        buttonAnimator.SetTrigger("Clicked");
        SetLoginButtonState(false);

        try
        {
            // Validate Firebase initialization
            if (db == null || auth == null)
            {
                Debug.LogError("Firebase not initialized properly.");
                feedbackText.text = "System not ready. Please restart the application.";
                return;
            }

            // Get and validate input
            string email = emailInput.text.Trim().ToLower();
            string password = passwordInput.text;

            if (!ValidateInput(email, password))
            {
                return; // Error messages already set in ValidateInput
            }

            Debug.Log($"Starting login process for email: {email}");
            feedbackText.text = "Checking account...";

            // STEP 1: Check if email is already registered in Firebase Auth
            bool isRegisteredInAuth = await CheckIfEmailExistsInAuth(email, password);

            if (isRegisteredInAuth)
            {
                // STEP 2: Email exists in Auth - attempt regular login
                Debug.Log("Email found in Firebase Auth. Attempting regular login...");
                await AttemptRegularLogin(email, password);
            }
            else
            {
                // STEP 3: Email not in Auth - check students collection for first login
                Debug.Log("Email not found in Firebase Auth. Checking for first login...");
                await AttemptFirstLogin(email, password);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Unexpected error in login process: {ex.Message}");
            feedbackText.text = "Login failed. Please try again.";
        }
        finally
        {
            // Re-enable login button
            SetLoginButtonState(true);
        }
    }

    private bool ValidateInput(string email, string password)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            Debug.LogWarning("Email or password field is empty.");
            feedbackText.text = "Please fill in all fields.";
            return false;
        }

        if (!IsValidEmail(email))
        {
            Debug.LogWarning("Invalid email format entered.");
            feedbackText.text = "Please enter a valid email address.";
            return false;
        }

        return true;
    }

    private async Task<bool> CheckIfEmailExistsInAuth(string email, string password)
    {
        try
        {
            Debug.Log($"Checking if email exists in Firebase Auth: {email},{password}");
            try
            {
                await auth.SignInWithEmailAndPasswordAsync(email, password);

                Debug.Log($"Email exists in Firebase Auth: true (unexpected successful login)");
                return true;

                // var signInMethods = await auth.FetchSignInMethodsForEmailAsync(email);
                // bool exists = signInMethods != null && signInMethods.Count > 0;
                // Debug.Log($"Email exists in Firebase Auth: {exists}");

                // return exists;
            }
            catch (FirebaseException authEx)
            {
                Debug.Log($"Auth check exception: {authEx.Message} (Code: {authEx.ErrorCode})");

                // Check specific error codes to determine if user exists
                switch (authEx.ErrorCode)
                {
                    case (int)AuthError.WrongPassword:
                        // Wrong password means the user exists
                        Debug.Log($"Email exists in Firebase Auth: true (wrong password error)");
                        return true;

                    case (int)AuthError.TooManyRequests:
                        // Too many requests - user likely exists but we can't verify
                        // Assume exists to try regular login path first
                        Debug.Log($"Email exists in Firebase Auth: true (too many requests - assuming exists)");
                        return true;

                    case (int)AuthError.UserDisabled:
                        // User exists but is disabled
                        Debug.Log($"Email exists in Firebase Auth: true (user disabled)");
                        return true;

                    case (int)AuthError.UserNotFound:
                        // User definitely doesn't exist in Auth
                        Debug.Log($"Email exists in Firebase Auth: false (user not found)");
                        return false;

                    default:
                        // For other errors, assume user doesn't exist in Auth
                        // This allows us to check the students collection
                        Debug.Log($"Email exists in Firebase Auth: false (other auth error: {authEx.ErrorCode})");
                        return false;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Unexpected error checking email in Auth: {ex.Message}");
            // On unexpected error, assume email doesn't exist in Auth
            // This allows us to proceed to check students collection
            return false;
        }
    }

    private async Task AttemptRegularLogin(string email, string password)
    {
        Debug.Log("=== Attempting Regular Login ===");

        try
        {
            feedbackText.text = "Logging in...";

            var userCredential = await auth.SignInWithEmailAndPasswordAsync(email, password);
            Debug.Log($"Regular login successful for user: {userCredential.User.UserId}");

            feedbackText.text = "Login successful! Loading...";
            await LoadMenuScene();
        }
        catch (FirebaseException ex)
        {
            Debug.LogWarning($"Regular login failed: {ex.Message} (Code: {ex.ErrorCode})");

            switch (ex.ErrorCode)
            {
                case (int)AuthError.WrongPassword:
                    feedbackText.text = "Incorrect password. Please try again.";
                    break;
                case (int)AuthError.TooManyRequests:
                    feedbackText.text = "Too many failed attempts. Please try again later.";
                    break;
                case (int)AuthError.UserDisabled:
                    feedbackText.text = "This account has been disabled. Please contact administrator.";
                    break;
                case (int)AuthError.UserNotFound:
                    feedbackText.text = "Account not found. Please check your email.";
                    break;
                default:
                    feedbackText.text = "Login failed. Please try again.";
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Unexpected error during regular login: {ex.Message}");
            feedbackText.text = "Login failed. Please try again.";
        }
    }

    private async Task AttemptFirstLogin(string email, string password)
    {
        Debug.Log("=== Attempting First Login ===");

        try
        {
            feedbackText.text = "Checking for first-time login...";

            // STEP 4: Search for student document using email as document ID
            DocumentReference studentDocRef = null;
            DocumentSnapshot studentDocSnap = null;
            string classCode = null;

            // Search through all classes for a student document with email as document ID
            QuerySnapshot classesSnapshot = await db.Collection("classes").GetSnapshotAsync();

            Debug.Log($"Searching through {classesSnapshot.Documents.Count()} classes for student with email: {email}");

            foreach (var classDoc in classesSnapshot.Documents)
            {
                Debug.Log($"Checking class: {classDoc.Id}");

                var tempStudentRef = classDoc.Reference.Collection("students").Document(email);
                var tempStudentSnap = await tempStudentRef.GetSnapshotAsync();

                if (tempStudentSnap.Exists)
                {
                    Debug.Log($"Student document found in class: {classDoc.Id}");
                    studentDocRef = tempStudentRef;
                    studentDocSnap = tempStudentSnap;
                    classCode = classDoc.GetValue<string>("classCode");
                    break;
                }
            }

            // STEP 5: Check if student document was found
            if (studentDocRef == null || !studentDocSnap.Exists)
            {
                Debug.LogWarning($"No student account found with email: {email}");
                feedbackText.text = "Invalid login credentials.";
                return;
            }

            // Validate student document for first login
            var studentData = studentDocSnap.ToDictionary();

            // Check if this is indeed a first login
            bool firstLogin = studentData.ContainsKey("firstLogin") &&
                             Convert.ToBoolean(studentData["firstLogin"]);

            if (!firstLogin)
            {
                Debug.LogWarning("Student account found but not marked as first login.");
                feedbackText.text = "Account already activated. Please use your regular password or contact administrator.";
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

            if (!VerifyPassword(password, storedTempPassword))
            {
                Debug.LogWarning("Incorrect temporary password entered.");
                feedbackText.text = "Incorrect temporary password.";
                return;
            }

            Debug.Log("Temporary password verified successfully.");

            // Store current session data for password change
            currentStudentRef = studentDocRef;
            currentEmail = email;
            pendingClassCode = classCode;

            // Show change password panel
            loginPanel.SetActive(false);
            changePasswordPanel.SetActive(true);
            changePassFeedbackText.text = "Please create a new password for your account.";

            // Clear password input fields
            newPasswordInput.text = "";
            confirmPasswordInput.text = "";
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


        buttonChangeAnimator.SetTrigger("Clicked");
        SetChangePasswordButtonState(false);

        try
        {
            string newPass = newPasswordInput.text;
            string confirmPass = confirmPasswordInput.text;

            // Validate password input
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
                changePassFeedbackText.text = "Password must be at least 6 characters long.";
                return;
            }

            changePassFeedbackText.text = "Creating your account...";

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
            studentData["timestamp"] = Timestamp.GetCurrentTimestamp();

            // Step 4: Create new student document with UID as document ID
            var classRef = currentStudentRef.Parent.Parent;
            var newStudentRef = classRef.Collection("students").Document(uid);

            Debug.Log($"Creating new student document with UID: {uid}");
            await newStudentRef.SetAsync(studentData, SetOptions.MergeAll);

            // Step 5: Delete old student document (with email as ID)
            Debug.Log($"Deleting old student document with email ID: {currentEmail}");
            await currentStudentRef.DeleteAsync();

            // Step 6: Log successful account activation
            await LogActivity(
                "First login complete",
                pendingClassCode,
                currentEmail,
                "First login completed successfully. Firebase Auth account created.",
                uid
            );

            Debug.Log("Account activation completed successfully.");
            changePasswordPanel.SetActive(false);

            changePassFeedbackText.text = "Account created successfully!";
            await LoadMenuScene();
        }
        catch (FirebaseException ex)
        {
            Debug.LogError($"Firebase Auth account creation failed: {ex.Message}");

            switch (ex.ErrorCode)
            {
                case (int)AuthError.WeakPassword:
                    changePassFeedbackText.text = "Password is too weak. Please choose a stronger password.";
                    break;
                case (int)AuthError.EmailAlreadyInUse:
                    changePassFeedbackText.text = "Email is already in use. Please contact administrator.";
                    break;
                default:
                    changePassFeedbackText.text = "Failed to create account. Please try again.";
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Unexpected error during password change: {ex.Message}");
            changePassFeedbackText.text = "Account creation failed. Please try again.";
        }
        finally
        {
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