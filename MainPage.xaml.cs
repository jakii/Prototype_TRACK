using MySql.Data.MySqlClient;
using System.Data;
using BCrypt.Net;

namespace TDMASApp
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
            Shell.Current.FlyoutBehavior = FlyoutBehavior.Disabled;
            Shell.SetBackButtonBehavior(this, new BackButtonBehavior
            {
                IsVisible = false,
                IsEnabled = false
            });
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            EmailEntry.Text = string.Empty;
            PasswordEntry.Text = string.Empty;
        }

        private async void OnLoginClicked(object sender, EventArgs e)
        {
            string connStr = "server=localhost;user=root;password=;database=dms_db;";
            string email = EmailEntry.Text;
            string password = PasswordEntry.Text;

            if (string.IsNullOrWhiteSpace(email))
            {
                await DisplayAlert("Validation Error", "Please enter your email.", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                await DisplayAlert("Validation Error", "Please enter your password.", "OK");
                return;
            }

            try
            {
                using MySqlConnection conn = new MySqlConnection(connStr);
                await conn.OpenAsync();

                string query = "SELECT id, password, role FROM users WHERE email = @email";
                using MySqlCommand cmd = new MySqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@email", email);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    string storedHash = reader.GetString("password");
                    int userId = reader.GetInt32("id");
                    string role = reader.GetString("role");

                    bool isPasswordValid = BCrypt.Net.BCrypt.Verify(password, storedHash);
                    if (isPasswordValid)
                    {
                        // ✅ Store user info globally
                        SessionManager.CurrentUserId = userId;
                        SessionManager.CurrentUserRole = role;

                        await LogSuccessfulLogin(userId, role, connStr);
                        await DisplayAlert("Success", "Login successful", "OK");

                        if (role == "Admin" || role == "SuperAdmin")
                        {
                            await Navigation.PushAsync(new AdminDashboardPage());
                        }
                        else
                        {
                            await Navigation.PushAsync(new HomePage());
                        }

                        Shell.Current.FlyoutBehavior = FlyoutBehavior.Flyout;
                        return;
                    }
                }

                await LogFailedLogin(email, connStr);
                await DisplayAlert("Error", "Invalid credentials", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }

        private async Task LogSuccessfulLogin(int userId, string role, string connectionString)
        {
            if (role == "SuperAdmin")
                return;

            try
            {
                using var conn = new MySqlConnection(connectionString);
                await conn.OpenAsync();

                var query = @"
                    INSERT INTO activity_logs 
                    (user_id, action, details, timestamp) 
                    VALUES 
                    (@userId, 'login_success', @details, UTC_TIMESTAMP())";

                using var cmd = new MySqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@userId", userId);
                cmd.Parameters.AddWithValue("@details", $"User logged in as {role}");
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {}
        }

        private async Task LogFailedLogin(string email, string connectionString)
        {
            try
            {
                using var conn = new MySqlConnection(connectionString);
                await conn.OpenAsync();

                var query = @"
                    INSERT INTO activity_logs 
                    (user_id, action, details, timestamp)
                    VALUES 
                    (NULL, 'login_failed', @details, UTC_TIMESTAMP())";

                using var cmd = new MySqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@details", $"Failed login attempt using: {email}");
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {}
        }

        private async void OnRegisterClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new RegisterPage());
        }
        private void OnShowPasswordCheckedChanged(object sender, CheckedChangedEventArgs e)
        {
            bool showPassword = e.Value;
            PasswordEntry.IsPassword = !showPassword;
        }
    }
}
