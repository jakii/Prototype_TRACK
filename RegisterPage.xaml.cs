using MySql.Data.MySqlClient;
namespace TDMASApp;

public partial class RegisterPage : ContentPage
{
    public RegisterPage()
    {
        InitializeComponent();
        Shell.Current.FlyoutBehavior = FlyoutBehavior.Disabled;
        Shell.SetBackButtonBehavior(this, new BackButtonBehavior
        {
            IsVisible = false,
            IsEnabled = false
        });
    }

    private async void RegisterButton_Clicked(object sender, EventArgs e)
    {
        string connStr = "server=localhost;user=root;password=;database=dms_db;";
        string username = UsernameEntry.Text;
        string email = EmailEntry.Text;
        string password = PasswordEntry.Text;
        string confirmPassword = ConfirmPasswordEntry.Text;
        string role = "User";

        //validation
        if (string.IsNullOrWhiteSpace(username))
        {
            await DisplayAlert("Error", "Username cannot be empty", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(email) || !email.Contains("@"))
        {
            await DisplayAlert("Error", "Please enter a valid email address", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            await DisplayAlert("Error", "Password must be at least 6 characters", "OK");
            return;
        }

        if (password != confirmPassword)
        {
            await DisplayAlert("Error", "Passwords do not match", "OK");
            return;
        }

        try
        {
            using MySqlConnection conn = new MySqlConnection(connStr);
            await conn.OpenAsync();

            // Hash the password using bcrypt
            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(password);

            string query = "INSERT INTO users (username, email, password, role) VALUES (@username, @email, @password, @role)";
            using MySqlCommand cmd = new MySqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@username", username);
            cmd.Parameters.AddWithValue("@email", email);
            cmd.Parameters.AddWithValue("@password", hashedPassword);
            cmd.Parameters.AddWithValue("@role", role);

            int result = await cmd.ExecuteNonQueryAsync();
            if (result > 0)
            {
                await DisplayAlert("Success", "Registration successful", "OK");
                await Navigation.PushAsync(new MainPage());
            }
            else
            {
                await DisplayAlert("Failed", "Registration failed", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }
    private void OnShowPasswordCheckedChanged(object sender, CheckedChangedEventArgs e)
    {
        bool showPassword = e.Value;
        PasswordEntry.IsPassword = !showPassword;
        ConfirmPasswordEntry.IsPassword = !showPassword;
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new MainPage());
    }
}
