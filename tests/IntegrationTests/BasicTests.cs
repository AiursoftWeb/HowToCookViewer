using System.Net;

[assembly:DoNotParallelize]

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class BasicTests : TestBase
{
    [TestMethod]
    [DataRow("/")]
    [DataRow("/hOmE?aaaaaa=bbbbbb")]
    [DataRow("/hOmE/InDeX")]
    public async Task GetHome(string url)
    {
        var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task RegisterAndLoginAndLogOffTest()
    {
        var expectedUserName = $"test-{Guid.NewGuid()}";
        var email = $"{expectedUserName}@aiursoft.com";
        var password = "Test-Password-123";

        // Step 1: Register a new user and assert a successful redirect.
        var registerResponse = await PostForm("/Account/Register", new Dictionary<string, string>
        {
            { "Email", email },
            { "Password", password },
            { "ConfirmPassword", password }
        });
        AssertRedirect(registerResponse, "/Dashboard/Index");

        // Step 2: Log off the user and assert a successful redirect.
        var homePageResponse = await Http.GetAsync("/Manage/Index");
        homePageResponse.EnsureSuccessStatusCode();
        
        var logOffResponse = await Http.GetAsync("/Account/LogOff");
        AssertRedirect(logOffResponse, "/");

        // Step 3: Log in with the newly created user and assert a successful redirect.
        var loginResponse = await PostForm("/Account/Login", new Dictionary<string, string>
        {
            { "EmailOrUserName", email },
            { "Password", password }
        });
        AssertRedirect(loginResponse, "/Dashboard/Index");

        // Step 4: Verify the final login state by checking the home page content.
        var finalHomePageResponse = await Http.GetAsync("/dashboard/index");
        finalHomePageResponse.EnsureSuccessStatusCode();
        var finalHtml = await finalHomePageResponse.Content.ReadAsStringAsync();
        Assert.Contains(expectedUserName, finalHtml);
    }

    [TestMethod]
    public async Task LoginWithInvalidCredentialsTest()
    {
        // Step 1: Attempt to log in with credentials for a user that does not exist.
        var email = $"test-{Guid.NewGuid()}@aiursoft.com";
        var password = "Wrong-Password-123";
        var loginResponse = await PostForm("/Account/Login", new Dictionary<string, string>
        {
            { "EmailOrUserName", email },
            { "Password", password }
        });

        // Step 2: Assert that the login fails and the correct error message is displayed.
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var html = await loginResponse.Content.ReadAsStringAsync();
        Assert.Contains("Invalid login attempt.", html);
    }

    [TestMethod]
    public async Task RegisterWithExistingEmailTest()
    {
        // Step 1: Register a new user successfully.
        var email = $"test-{Guid.NewGuid()}@aiursoft.com";
        var password = "Test-Password-123";
        var registerResponse = await PostForm("/Account/Register", new Dictionary<string, string>
        {
            { "Email", email },
            { "Password", password },
            { "ConfirmPassword", password }
        });
        Assert.AreEqual(HttpStatusCode.Found, registerResponse.StatusCode);

        // Step 2: Log off to clear the current session.
await PostForm("/Account/LogOff", new Dictionary<string, string>(), includeToken: false);

        // Step 3: Attempt to register again using the same email.
        var secondRegisterResponse = await PostForm("/Account/Register", new Dictionary<string, string>
        {
            { "Email", email },
            { "Password", password },
            { "ConfirmPassword", password }
        });

        // Step 4: Assert the registration fails and the correct error message is displayed.
        Assert.AreEqual(HttpStatusCode.OK, secondRegisterResponse.StatusCode);
        var html = await secondRegisterResponse.Content.ReadAsStringAsync();
        Assert.Contains("The username already exists. Please try another username.", html);
    }

    [TestMethod]
    public async Task LoginWithExistingUserAndWrongPasswordTest()
    {
        // Step 1: Register a new user.
        var email = $"test-{Guid.NewGuid()}@aiursoft.com";
        var correctPassword = "Test-Password-123";
        var wrongPassword = "Wrong-Password-456";
        var registerResponse = await PostForm("/Account/Register", new Dictionary<string, string>
        {
            { "Email", email },
            { "Password", correctPassword },
            { "ConfirmPassword", correctPassword }
        });
        Assert.AreEqual(HttpStatusCode.Found, registerResponse.StatusCode);

        // Step 2: Log off the user.
await PostForm("/Account/LogOff", new Dictionary<string, string>(), includeToken: false);

        // Step 3: Attempt to log in with the correct email but a wrong password.
        var loginResponse = await PostForm("/Account/Login", new Dictionary<string, string>
        {
            { "EmailOrUserName", email },
            { "Password", wrongPassword }
        });

        // Step 4: Assert the login fails and displays an error message.
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var html = await loginResponse.Content.ReadAsStringAsync();
        Assert.Contains("Invalid login attempt.", html);
    }

    [TestMethod]
    public async Task AccountLockoutTest()
    {
        // Using the default setting for max failed access attempts in ASP.NET Core Identity.
        const int maxFailedAccessAttempts = 5;

        // Step 1: Register a new user and then log off.
        var email = $"test-{Guid.NewGuid()}@aiursoft.com";
        var correctPassword = "Test-Password-123";
        var wrongPassword = "Wrong-Password-456";
        await PostForm("/Account/Register", new Dictionary<string, string>
        {
            { "Email", email },
            { "Password", correctPassword },
            { "ConfirmPassword", correctPassword }
        });
await PostForm("/Account/LogOff", new Dictionary<string, string>(), includeToken: false);

        // Step 2: Attempt to log in with the wrong password multiple times to trigger lockout.
        HttpResponseMessage loginResponse = null!;
        for (int i = 0; i < maxFailedAccessAttempts; i++)
        {
            loginResponse = await PostForm("/Account/Login", new Dictionary<string, string>
            {
                { "EmailOrUserName", email },
                { "Password", wrongPassword }
            });
        }

        // Step 3: Assert that the account is now locked.
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var html = await loginResponse.Content.ReadAsStringAsync();
        Assert.Contains("This account has been locked out, please try again later.", html);

        // Step 4: Verify that logging in with the correct password also fails while the account is locked.
        var finalLoginResponse = await PostForm("/Account/Login", new Dictionary<string, string>
        {
            { "EmailOrUserName", email },
            { "Password", correctPassword }
        });
        var finalHtml = await finalLoginResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, finalLoginResponse.StatusCode);
        Assert.Contains("This account has been locked out, please try again later.", finalHtml);
    }

    [TestMethod]
    public async Task ChangePasswordSuccessfullyTest()
    {
        // Step 1: Register and log in a new user, storing their credentials for the entire test.
        var (email, oldPassword) = await RegisterAndLoginAsync();
        var newPassword = "New-Password-456";

        // Step 2: Post the form to change the password.
        var changePasswordResponse = await PostForm("/Manage/ChangePassword", new Dictionary<string, string>
        {
            { "OldPassword", oldPassword },
            { "NewPassword", newPassword },
            { "ConfirmPassword", newPassword }
        });

        // Step 3: Assert the password change was successful and redirected correctly.
        AssertRedirect(changePasswordResponse, "/Manage?Message=ChangePasswordSuccess");

        // Step 4: Log off to test the new password.
await PostForm("/Account/LogOff", new Dictionary<string, string>(), includeToken: false);

        // Step 5: Verify that the old password no longer works for the original user.
        var oldLoginResponse = await PostForm("/Account/Login", new Dictionary<string, string>
        {
            { "EmailOrUserName", email },
            { "Password", oldPassword }
        });
        Assert.AreEqual(HttpStatusCode.OK, oldLoginResponse.StatusCode);

        // Step 6: Verify that the new password works for the original user.
        var newLoginResponse = await PostForm("/Account/Login", new Dictionary<string, string>
        {
            { "EmailOrUserName", email },
            { "Password", newPassword }
        });
        AssertRedirect(newLoginResponse, "/Dashboard/Index");
    }

    [TestMethod]
    public async Task ChangeProfileSuccessfullyTest()
    {
        // Step 1: Register and log in a new user.
        var (email, _) = await RegisterAndLoginAsync();
        var originalUserName = email.Split('@')[0];
        var newUserName = $"new-name-{new Random().Next(1000, 9999)}";

        // Step 2: Post the form to change the user's display name.
        var changeProfileResponse = await PostForm("/Manage/ChangeProfile", new Dictionary<string, string>
        {
            { "Name", newUserName }
        });

        // Step 3: Assert the profile change was successful and redirected correctly.
        AssertRedirect(changeProfileResponse, "/Manage?Message=ChangeProfileSuccess");

        // Step 4: Visit the home page and verify the new name is displayed.
        var homePageResponse = await Http.GetAsync("/dashboard/index");
        homePageResponse.EnsureSuccessStatusCode();
        var html = await homePageResponse.Content.ReadAsStringAsync();
        Assert.Contains(newUserName, html);
        Assert.DoesNotContain(originalUserName, html);
    }
}
