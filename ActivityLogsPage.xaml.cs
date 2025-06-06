using Microsoft.Maui.Controls;
using MySql.Data.MySqlClient;
using System.Collections.ObjectModel;

namespace TDMASApp;

public partial class ActivityLogsPage : ContentPage
{
    public ObservableCollection<ActivityLogDisplay> ActivityLogs { get; } = new ObservableCollection<ActivityLogDisplay>();
    private bool _isLoading;
    private DateTime? _lastRefreshTime;

    public ActivityLogsPage()
    {
        InitializeComponent();
        BindingContext = this;
        LoadLogsCommand = new Command(async () => await LoadLogsAsync());
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!ActivityLogs.Any())
        {
            _ = LoadLogsAsync();
        }
    }

    public Command LoadLogsCommand { get; }

    private async Task LoadLogsAsync()
    {
        if (_isLoading) return;

        _isLoading = true;
        IsBusy = true;
        _lastRefreshTime = DateTime.Now;

        try
        {
            ActivityLogs.Clear();

            using var conn = new MySqlConnection("server=localhost;user=root;password=;database=dms_db;");
            await conn.OpenAsync();

            string query = @"
                SELECT u.email, u.role, a.action, a.timestamp, a.details
                FROM activity_logs a
                LEFT JOIN users u ON a.user_id = u.id
                ORDER BY a.timestamp DESC";

            using var cmd = new MySqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var role = reader["role"]?.ToString() ?? "N/A";

                if (role == "SuperAdmin")
                    continue;

                ActivityLogs.Add(new ActivityLogDisplay
                {
                    UserName = reader["email"]?.ToString() ?? "System",
                    Action = GetActionDisplayName(reader["action"]?.ToString() ?? "Unknown"),
                    Timestamp = Convert.ToDateTime(reader["timestamp"]),
                    Details = reader["details"]?.ToString() ?? "",
                    Role = role
                });
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to load logs: {ex.Message}", "OK");
        }
        finally
        {
            _isLoading = false;
            IsBusy = false;
            OnPropertyChanged(nameof(LastRefreshText));
        }
    }

    private string GetActionDisplayName(string action)
    {
        return action switch
        {
            "login_success" => "User Login",
            "login_failed" => "Login Failed",
            "document_view" => "Opened Document",
            "document_download" => "Downloaded Document",
            "document_rename" => "Renamed Document",
            "document_details" => "Viewed Details",
            "document_upload" => "Uploaded Documents",
            _ => action
        };
    }

    public string LastRefreshText => _lastRefreshTime.HasValue
        ? $"Last refreshed: {_lastRefreshTime.Value:MMM dd, yyyy hh:mm tt}"
        : "Not refreshed yet";

    public class ActivityLogDisplay
    {
        public string? UserName { get; set; }
        public string? Action { get; set; }
        public DateTime Timestamp { get; set; }
        public string? Details { get; set; }
        public string? Role { get; set; }
    }
}
