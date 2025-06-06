using MySql.Data.MySqlClient;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using System.Linq;

namespace TDMASApp;

public partial class HomePage : TabbedPage
{
    private const string ConnectionString = "server=localhost;port=3306;database=dms_db;user=root;password=;";
    private byte[]? _selectedFileBytes;
    private string? _selectedFileName;
   

    public static Users CurrentUser { get; set; }
    public ObservableCollection<DocumentCategory> Categories { get; } = new();
    public ObservableCollection<UserDocument> Documents { get; } = new();
    public Command RefreshCommand { get; }
    public ObservableCollection<SharedDocument> SharedDocuments { get; } = new();
    public Command RefreshSharedCommand { get; }

    public HomePage()
    {
        InitializeComponent();
        BindingContext = this;

        RefreshCommand = new Command(async () => await LoadDocuments());
        _ = LoadCategories();

        Shell.Current.FlyoutBehavior = FlyoutBehavior.Flyout;
        Shell.SetBackButtonBehavior(this, new BackButtonBehavior
        {
            IsVisible = false,
            IsEnabled = false
        });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadDocuments();
        LoadSharedDocuments();
    }

    private async Task LoadCategories()
    {
        try
        {
            Categories.Clear();
            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();
            using var command = new MySqlCommand("SELECT id, name FROM document_categories", connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                Categories.Add(new DocumentCategory
                {
                    Id = reader.GetInt32("id"),
                    Name = reader.GetString("name")
                });
            }

            CategoryPicker.ItemsSource = Categories;
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private async Task LoadDocuments()
    {
        try
        {
            Documents.Clear();

            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();

            using var command = new MySqlCommand(
                "SELECT d.id, d.title, d.file_name, d.file_content, d.upload_date, " +
                "c.name as category_name, u.username as uploader_name " +
                "FROM documents d " +
                "JOIN document_categories c ON d.category_id = c.id " +
                "LEFT JOIN users u ON d.uploaded_by = u.id " +
                "ORDER BY d.upload_date DESC",
                connection);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var fileName = reader.GetString("file_name");
                var extension = Path.GetExtension(fileName).ToLower();

                string previewImage = extension switch
                {
                    ".pdf" => "pdf_icon.png",
                    ".doc" or ".docx" => "docx_icon.png",
                    ".xls" or ".xlsx" => "xlsx.png",
                    ".ppt" or ".pptx" => "pptx.png",
                    _ => "file_icon.png"
                };

                Documents.Add(new UserDocument
                {
                    Id = reader.GetInt32("id"),
                    Title = reader.GetString("title"),
                    FileName = fileName,
                    FileBytes = reader["file_content"] as byte[],
                    CategoryName = reader.GetString("category_name"),
                    UploadDate = reader.GetDateTime("upload_date"),
                    PreviewImage = previewImage,
                    UploaderName = reader.IsDBNull("uploader_name")
                        ? "System"
                        : reader.GetString("uploader_name")
                });
            }

            FilterDocuments(SearchBar.Text);
            EmptyStateView.IsVisible = Documents.Count == 0;
            DocumentsCollection.IsVisible = Documents.Count > 0;
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to load documents: {ex.Message}", "OK");
            await LogActivityAsync(SessionManager.CurrentUserId, "document_load_error",
                $"Error loading documents: {ex.Message}");
        }
        finally
        {
            RefreshView.IsRefreshing = false;
        }
    }
    private void FilterDocuments(string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            DocumentsCollection.ItemsSource = Documents;
        }
        else
        {
            var filtered = Documents.Where(d =>
                (!string.IsNullOrEmpty(d.Title) && d.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(d.CategoryName) && d.CategoryName.Contains(searchText, StringComparison.OrdinalIgnoreCase)) ||
                d.UploadDate.ToString("MMM dd, yyyy").Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .ToList();

            DocumentsCollection.ItemsSource = filtered;
        }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        FilterDocuments(e.NewTextValue);
    }

    private async void OnSelectFileClicked(object sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync();
            if (result != null)
            {
                _selectedFileName = result.FileName;
                using var stream = await result.OpenReadAsync();
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                _selectedFileBytes = memoryStream.ToArray();
                SelectedFileLabel.Text = $"Selected: {_selectedFileName}";
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private async void OnUploadDocumentClicked(object sender, EventArgs e)
    {
        try
        {
            if (CategoryPicker.SelectedItem is not DocumentCategory category)
            {
                await DisplayAlert("Error", "Please select a category", "OK");
                await LogActivityAsync(SessionManager.CurrentUserId, "upload_failed", "No category selected");
                return;
            }

            if (_selectedFileBytes == null)
            {
                await DisplayAlert("Error", "Please select a file first", "OK");
                await LogActivityAsync(SessionManager.CurrentUserId, "upload_failed", "No file selected");
                return;
            }

            var title = await DisplayPromptAsync("Title", "Enter document title:", "Upload", "Cancel");
            if (string.IsNullOrWhiteSpace(title))
            {
                await LogActivityAsync(SessionManager.CurrentUserId, "upload_cancelled", "User cancelled upload");
                return;
            }

            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();

            using (var checkCommand = new MySqlCommand("SELECT COUNT(*) FROM users WHERE id = @userId", connection))
            {
                checkCommand.Parameters.AddWithValue("@userId", SessionManager.CurrentUserId);
                var result = (long)(await checkCommand.ExecuteScalarAsync());

                if (result == 0)
                {
                    await DisplayAlert("Error", "Invalid user. Cannot upload document.", "OK");
                    await LogActivityAsync(SessionManager.CurrentUserId, "upload_failed", "Invalid user ID");
                    return;
                }
            }

            await LogActivityAsync(SessionManager.CurrentUserId, "upload_started", $"Starting upload: {title} ({_selectedFileName})");

            string description = DocumentDescriptionEditor.Text;

            using var command = new MySqlCommand(
                "INSERT INTO documents (title, file_name, file_content, category_id, upload_date, description, uploaded_by) " +
                "VALUES (@title, @fileName, @fileContent, @categoryId, @date, @description, @uploadBy); " +
                "SELECT LAST_INSERT_ID();",
                connection);

            command.Parameters.AddWithValue("@title", title);
            command.Parameters.AddWithValue("@fileName", _selectedFileName);
            command.Parameters.AddWithValue("@fileContent", _selectedFileBytes);
            command.Parameters.AddWithValue("@categoryId", category.Id);
            command.Parameters.AddWithValue("@date", DateTime.Now);
            command.Parameters.AddWithValue("@description", string.IsNullOrWhiteSpace(description) ? DBNull.Value : description);
            command.Parameters.AddWithValue("@uploadBy", SessionManager.CurrentUserId);

            var newDocumentId = Convert.ToInt32(await command.ExecuteScalarAsync());

            _selectedFileBytes = null;
            _selectedFileName = null;
            SelectedFileLabel.Text = "No file selected";

            await LogActivityAsync(SessionManager.CurrentUserId, "upload_success",
                $"Document uploaded. ID: {newDocumentId}, Title: {title}");

            await LoadDocuments();
            await DisplayAlert("Success", $"Document uploaded successfully by User ID: {SessionManager.CurrentUserId}", "OK");
        }
        catch (Exception ex)
        {
            await LogActivityAsync(SessionManager.CurrentUserId, "upload_failed",
                $"Error uploading {_selectedFileName}: {ex.Message}");
            await DisplayAlert("Error", $"Upload failed: {ex.Message}", "OK");
        }
    }
    private async void OnDocumentSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is UserDocument document)
        {
            await ShowMoreOptionsAsync(document);
            ((CollectionView)sender).SelectedItem = null;
        }
    }
    private async Task ShowMoreOptionsAsync(UserDocument document)
    {
        var action = await DisplayActionSheet(
            $"Options: {document.Title}",
            "Cancel",
            null,
            "Open",
            "Download",
            "View Details",
            "Rename");

        switch (action)
        {
            case "Open":
                await OpenDocumentAsync(document.FileBytes, document.FileName);
                await LogActivityAsync(SessionManager.CurrentUserId, "document_view", $"Opened document '{document.Title}'");
                break;

            case "Download":
                await DownloadDocumentAsync(document.FileBytes, document.FileName);
                await LogActivityAsync(SessionManager.CurrentUserId, "document_download", $"Downloaded document '{document.Title}'");
                break;

            case "View Details":
                await DisplayAlert("Details",
     $"Title: {document.Title}\n" +
     $"Category: {document.CategoryName}\n" +
     $"File: {document.FileName}\n" +
     $"Size: {document.FileSizeDisplay}\n" +
     $"Uploaded By: {document.UploaderName}\n" +
     $"Upload Date: {document.UploadDate:MMM dd, yyyy}",
     "OK");
                await LogActivityAsync(SessionManager.CurrentUserId, "document_details", $"Viewed details of document '{document.Title}'");
                break;

            case "Rename":
                await RenameDocument(document);
                break;

            case "Share":
                await ShareDocumentAsync(document);
                await LogActivityAsync(SessionManager.CurrentUserId, "document_share", $"Shared document '{document.Title}'");
                break;
        }
    }

    private async Task OpenDocumentAsync(byte[] fileBytes, string fileName)
    {
        try
        {
            var filePath = Path.Combine(FileSystem.CacheDirectory, fileName);
            await File.WriteAllBytesAsync(filePath, fileBytes);

            await Launcher.OpenAsync(new OpenFileRequest
            {
                File = new ReadOnlyFile(filePath),
                Title = "Open Document"
            });

            await LogActivityAsync(SessionManager.CurrentUserId, "document_view", $"Opened document '{fileName}'");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Cannot open file: {ex.Message}", "OK");
        }
    }

    private async Task DownloadDocumentAsync(byte[] fileBytes, string fileName)
    {
        try
        {
            var downloadsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var filePath = Path.Combine(downloadsPath, fileName);
            await File.WriteAllBytesAsync(filePath, fileBytes);

            await DisplayAlert("Success", $"Saved to: {filePath}", "OK");

            await LogActivityAsync(SessionManager.CurrentUserId, "document_download", $"Downloaded document '{fileName}'");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Download failed: {ex.Message}", "OK");
        }
    }

    private async Task RenameDocument(UserDocument document)
    {
        var newName = await DisplayPromptAsync("Rename", "Enter new name:", initialValue: document.Title);
        if (!string.IsNullOrWhiteSpace(newName) && newName != document.Title)
        {
            try
            {
                using var connection = new MySqlConnection(ConnectionString);
                await connection.OpenAsync();

                using var cmd = new MySqlCommand(
                    "UPDATE documents SET title = @title WHERE id = @id",
                    connection);

                cmd.Parameters.AddWithValue("@title", newName);
                cmd.Parameters.AddWithValue("@id", document.Id);

                await cmd.ExecuteNonQueryAsync();
                await LoadDocuments();

                await LogActivityAsync(SessionManager.CurrentUserId, "document_rename", $"Renamed document '{document.Title}' to '{newName}'");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Rename failed: {ex.Message}", "OK");
            }
        }
    }
    private async Task LogActivityAsync(int? userId, string action, string details)
    {
        if (userId == null || userId <= 0)
        {
            Console.WriteLine("LogActivityAsync skipped: userId is null or invalid.");
            return;
        }

        try
        {
            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();

            using var cmd = new MySqlCommand(@"
                INSERT INTO activity_logs (user_id, action, details, timestamp)
                VALUES (@user_id, @action, @details, @timestamp)", connection);

            cmd.Parameters.AddWithValue("@user_id", userId);
            cmd.Parameters.AddWithValue("@action", action);
            cmd.Parameters.AddWithValue("@details", details);
            cmd.Parameters.AddWithValue("@timestamp", DateTime.UtcNow);

            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Log Error", $"Logging failed: {ex.Message}", "OK");
        }
    }

    private async Task ShareDocumentAsync(UserDocument document)
    {
        try
        {
            if (document?.FileBytes == null || document.FileBytes.Length == 0)
            {
                await DisplayAlert("Error", "Document content is empty", "OK");
                return;
            }

            // Prompt for email address
            var email = await DisplayPromptAsync("Share Document",
                "Enter recipient's email address:",
                "Share",
                "Cancel",
                keyboard: Keyboard.Email);

            if (string.IsNullOrWhiteSpace(email))
                return;

            // Validate email format
            if (!email.Contains("@") || !email.Contains("."))
            {
                await DisplayAlert("Invalid Email", "Please enter a valid email address", "OK");
                return;
            }

            // Check if sharing with self
            var currentUserEmail = await GetCurrentUserEmail();
            if (email.Equals(currentUserEmail, StringComparison.OrdinalIgnoreCase))
            {
                await DisplayAlert("Error", "You cannot share with yourself", "OK");
                return;
            }

            // Find user in database
            var user = await GetUserByEmail(email);
            if (user == null)
            {
                bool invite = await DisplayAlert("User Not Found",
                    "This user is not registered. Would you like to send an invitation?",
                    "Send Invite", "Cancel");

                if (invite)
                {
                    await SendInvitation(email, document.Title);
                }
                return;
            }

            // Share with existing usersss
            var result = await ShareWithUser(document, user);
            if (!result.Success)
            {
                await DisplayAlert("Notice", result.Message, "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Sharing failed: {ex.Message}", "OK");
        }
    }

    private async Task<User> GetUserByEmail(string email)
    {
        try
        {
            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();

            using var command = new MySqlCommand(
                "SELECT id, username, email FROM users WHERE email = @email AND is_active = 1",
                connection);

            command.Parameters.AddWithValue("@email", email);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new User
                {
                    Id = reader.GetInt32("id"),
                    Name = reader.GetString("username"),
                    Email = reader.GetString("email")
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error finding user: {ex.Message}");
        }
        return null;
    }

    private async Task<string> GetCurrentUserEmail()
    {
        return Preferences.Get("user_email", "");
    }

    private async Task SendInvitation(string email, string documentTitle)
    {
        try
        {
            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();

            using var command = new MySqlCommand(
                "INSERT INTO invitations (email, document_title, invited_by, invitation_date) " +
                "VALUES (@email, @title, @invitedBy, @date)",
                connection);

            command.Parameters.AddWithValue("@email", email);
            command.Parameters.AddWithValue("@title", documentTitle);
            command.Parameters.AddWithValue("@invitedBy", GetCurrentUserId());
            command.Parameters.AddWithValue("@date", DateTime.Now);

            await command.ExecuteNonQueryAsync();
            await DisplayAlert("Invitation Sent", $"An invitation has been sent to {email}", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to send invitation: {ex.Message}", "OK");
        }
    }

    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
    }

    private int GetCurrentUserId()
    {
        return Preferences.Get("user_id", 0);
    }

    private async Task<ShareResult> ShareWithUser(UserDocument document, User user)
    {
        try
        {
            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();

            // Check if already shared
            using var checkCmd = new MySqlCommand(
                "SELECT COUNT(*) FROM shared_documents WHERE document_id = @docId AND shared_with_user_id = @userId",
                connection);

            checkCmd.Parameters.AddWithValue("@docId", document.Id);
            checkCmd.Parameters.AddWithValue("@userId", user.Id);

            if (Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0)
            {
                return new ShareResult { Success = false, Message = "Already shared with this user" };
            }

            // Insert share record
            using var cmd = new MySqlCommand(
                "INSERT INTO shared_documents (document_id, shared_with_user_id, shared_by_user_id, shared_date) " +
                "VALUES (@docId, @sharedWith, @sharedBy, @date)",
                connection);

            cmd.Parameters.AddWithValue("@docId", document.Id);
            cmd.Parameters.AddWithValue("@sharedWith", user.Id);
            cmd.Parameters.AddWithValue("@sharedBy", GetCurrentUserId());
            cmd.Parameters.AddWithValue("@date", DateTime.Now);

            await cmd.ExecuteNonQueryAsync();

            return new ShareResult
            {
                Success = true,
                Message = $"Shared with {user.Name}"
            };
        }
        catch (Exception ex)
        {
            return new ShareResult
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }
    private async Task LoadSharedDocuments()
    {
        try
        {
            SharedDocuments.Clear();

            var currentUserId = GetCurrentUserId();
            Console.WriteLine($"Loading shared documents for user ID: {currentUserId}");

            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();

            string query = @"
            SELECT d.id, d.title, d.file_name, d.file_content, d.upload_date, 
                   c.name as category_name, u.username as shared_by_name,
                   sd.shared_date
            FROM shared_documents sd
            JOIN documents d ON sd.document_id = d.id
            JOIN document_categories c ON d.category_id = c.id
            JOIN users u ON sd.shared_by_user_id = u.id
            WHERE sd.shared_with_user_id = @userId
            ORDER BY sd.shared_date DESC";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@userId", currentUserId);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var fileName = reader.GetString("file_name");
                var extension = Path.GetExtension(fileName).ToLower();

                string previewImage = extension switch
                {
                    ".pdf" => "pdf_icon.png",
                    ".doc" or ".docx" => "docx_icon.png",
                    ".xls" or ".xlsx" => "xlsx.png",
                    ".ppt" or ".pptx" => "pptx.png",
                    _ => "file_icon.png"
                };

                // Safely get file_content bytes, handle nulls gracefully
                byte[] fileBytes = null;
                if (!reader.IsDBNull(reader.GetOrdinal("file_content")))
                {
                    fileBytes = (byte[])reader["file_content"];
                }

                SharedDocuments.Add(new SharedDocument
                {
                    Id = reader.GetInt32("id"),
                    Title = reader.GetString("title"),
                    FileName = fileName,
                    FileBytes = fileBytes,
                    CategoryName = reader.GetString("category_name"),
                    UploadDate = reader.GetDateTime("upload_date"),
                    PreviewImage = previewImage,
                    SharedByName = reader.GetString("shared_by_name"),
                    SharedDate = reader.GetDateTime("shared_date")
                });
            }

            Console.WriteLine($"Loaded {SharedDocuments.Count} shared documents.");

            //SharedDocsEmptyView.IsVisible = SharedDocuments.Count == 0;
            //SharedDocsCollection.IsVisible = SharedDocuments.Count > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading shared documents: {ex}");
            await DisplayAlert("Error", $"Failed to load shared documents: {ex.Message}", "OK");
        }
        finally
        {
        }
    }


    private async void OnSharedDocumentSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is SharedDocument document)
        {
            await ShowSharedDocumentOptions(document);
            ((CollectionView)sender).SelectedItem = null;
        }
    }

    private async void OnSharedDocumentOptionsClicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is SharedDocument document)
        {
            await ShowSharedDocumentOptions(document);
        }
    }

    private async Task ShowSharedDocumentOptions(SharedDocument document)
    {
        var action = await DisplayActionSheet(
            $"Options: {document.Title}",
            "Cancel",
            null,
            "Open",
            "Download",
            "View Details");

        switch (action)
        {
            case "Open":
                await OpenDocumentAsync(document.FileBytes, document.FileName);
                break;
            case "Download":
                await DownloadDocumentAsync(document.FileBytes, document.FileName);
                break;
            case "View Details":
                await DisplayAlert("Details",
                    $"Title: {document.Title}\n" +
                    $"Category: {document.CategoryName}\n" +
                    $"File: {document.FileName}\n" +
                    $"Size: {document.FileSizeDisplay}\n" +
                    $"Uploaded: {document.UploadDate:MMM dd, yyyy}\n" +
                    $"Shared by: {document.SharedByName}\n" +
                    $"Shared on: {document.SharedDate:MMM dd, yyyy}",
                    "OK");
                break;
        }


    }

    public class SharedDocument : UserDocument
    {
        public string SharedByName { get; set; }
        public DateTime SharedDate { get; set; }
    }
    public class ShareResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
    }

    public class Users
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
    }
    private async void OnLogoutAppearing(object sender, EventArgs e)
    {
        if (await DisplayAlert("Logout", "Are you sure?", "Yes", "No"))
        {
            Preferences.Clear();
            await Shell.Current.GoToAsync("//MainPage");
            Application.Current.MainPage = new AppShell();
        }
        else
        {
            CurrentPage = Children[0];
        }
    }
    public class DocumentCategory
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    public class UserDocument
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public string? FileName { get; set; }
        public string? CategoryName { get; set; }
        public DateTime UploadDate { get; set; }
        public byte[]? FileBytes { get; set; }
        public string? PreviewImage { get; set; }
        public string UploaderName { get; set; }

        public string FileSizeDisplay => FileBytes != null ?
            (FileBytes.Length >= 1024 * 1024 ?
                $"{FileBytes.Length / (1024f * 1024f):0.0} MB" :
                $"{FileBytes.Length / 1024f:0.0} KB") :
            "0 KB";
    }
}