using MySql.Data.MySqlClient;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Text;
using System.Windows.Input;
using BCrypt.Net;
using System.Security.Cryptography;

namespace TDMASApp
{
    public partial class AdminDashboardPage : ContentPage, INotifyPropertyChanged
    {
        private const string ConnectionString = "server=localhost;port=3306;database=dms_db;user=root;password=;";
        private bool _isLoading = false;
        private bool _isBusy;
        public bool NoDocumentsVisible { get; set; }

        public ObservableCollection<User> Users { get; } = new ObservableCollection<User>();
        public ObservableCollection<Document> Documents { get; } = new ObservableCollection<Document>();
        public ObservableCollection<Category> Categories { get; } = new ObservableCollection<Category>();
        public ObservableCollection<StorageUsage> StorageUsageByUser { get; } = new ObservableCollection<StorageUsage>();

        // Storage Monitoring Properties
        private double _storageUsagePercentage;
        public double StorageUsagePercentage
        {
            get => _storageUsagePercentage;
            set
            {
                if (_storageUsagePercentage != value)
                {
                    _storageUsagePercentage = value;
                    OnPropertyChanged(nameof(StorageUsagePercentage));
                    OnPropertyChanged(nameof(StorageProgressColor));
                    OnPropertyChanged(nameof(StorageUsageText));
                }
            }
        }

        public Color StorageProgressColor => StorageUsagePercentage switch
        {
            > 0.9 => Colors.Red,
            > 0.7 => Colors.Orange,
            _ => Colors.Green
        };

        public string StorageUsageText
        {
            get
            {
                double totalGB = 10;
                double usedGB = totalGB * StorageUsagePercentage;
                return $"{usedGB:0.0} GB / {totalGB} GB";
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy != value)
                {
                    _isBusy = value;
                    OnPropertyChanged(nameof(IsBusy));
                }
            }
        }

        public AdminDashboardPage()
        {
            InitializeComponent();
            BindingContext = this;
            LoadData();

            Shell.SetBackButtonBehavior(this, new BackButtonBehavior
            {
                IsVisible = false,
                IsEnabled = false
            });
        }

        private async void LoadData()
        {
            if (_isLoading) return;

            _isLoading = true;
            IsBusy = true;

            try
            {
                await Task.WhenAll(
                    LoadUsers(),
                    LoadDocuments(),
                    LoadCategories()
                    //LoadStorageData()
                );
            }
            finally
            {
                IsBusy = false;
                _isLoading = false;
            }
        }


       /* private async Task LoadStorageData()
        {
            try
            {
                StorageUsageByUser.Clear();

                using var connection = new MySqlConnection(ConnectionString);
                await connection.OpenAsync();

                string query = @"
                    SELECT 
                        u.username,
                        SUM(LENGTH(d.file_content)) / (1024 * 1024) AS storage_used_mb,
                        (SUM(LENGTH(d.file_content)) / (1024 * 1024 * 1024 * 10)) * 100 AS percentage
                    FROM 
                        users u
                    LEFT JOIN 
                        documents d ON u.id = d.uploaded_by
                    GROUP BY 
                        u.username";

                using var command = new MySqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();

                double totalUsage = 0;

                while (await reader.ReadAsync())
                {
                    var storageUsage = new StorageUsage
                    {
                        Username = reader.GetString("username"),
                        StorageUsed = reader.GetDouble("storage_used_mb"),
                        Percentage = reader.GetDouble("percentage")
                    };

                    StorageUsageByUser.Add(storageUsage);
                    totalUsage += storageUsage.StorageUsed;
                }

                StorageUsagePercentage = totalUsage / (10 * 1024);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to load storage data: {ex.Message}", "OK");
            }
        }*/
        
        //Load Users
        private async Task LoadUsers()
        {
            try
            {
                Users.Clear();
                using var connection = new MySqlConnection(ConnectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(
                    "SELECT id, username, email, role FROM users WHERE role <> 'SuperAdmin'",
                    connection);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    Users.Add(new User
                    {
                        Id = reader.GetInt32("id"),
                        Username = reader.GetString("username"),
                        Email = reader.GetString("email"),
                        Role = reader.GetString("role")
                    });
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to load users: {ex.Message}", "OK");
            }
        }

        //Load Documents
        private async Task LoadDocuments()
        {
            try
            {
                Documents.Clear();

                using var connection = new MySqlConnection(ConnectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(
                    "SELECT d.id, d.title, d.file_name, d.file_content, d.upload_date, c.name as category_name " +
                    "FROM documents d JOIN document_categories c ON d.category_id = c.id", connection);

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

                    Documents.Add(new Document
                    {
                        Id = reader.GetInt32("id"),
                        Title = reader.GetString("title"),
                        FileName = fileName,
                        FileBytes = reader["file_content"] as byte[],
                        CategoryName = reader.GetString("category_name"),
                        UploadDate = reader.GetDateTime("upload_date"),
                        PreviewImage = previewImage
                    });
                }
                FilterDocuments(SearchBar.Text);
                NoDocumentsVisible = Documents.Count == 0;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to load documents: {ex.Message}", "OK");
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

        //load categories
        private async Task LoadCategories()
        {
            try
            {
                Categories.Clear();

                using var connection = new MySqlConnection(ConnectionString);
                await connection.OpenAsync();

                string query = @"
                    SELECT id, name
                    FROM document_categories;";

                using var command = new MySqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    Categories.Add(new Category
                    {
                        Id = reader.GetInt32("id"),
                        Name = reader.GetString("name")
                    });
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to load categories: {ex.Message}", "OK");
            }
        }
        private async Task EditCategory(Category category)
        {
            try
            {
                var newName = await DisplayPromptAsync("Edit Category", "New name:", initialValue: category.Name);
                if (string.IsNullOrWhiteSpace(newName)) return;

                using var connection = new MySqlConnection(ConnectionString);
                await connection.OpenAsync();

                string query = "UPDATE document_categories SET name = @name WHERE id = @id";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@name", newName);
                command.Parameters.AddWithValue("@id", category.Id);

                await command.ExecuteNonQueryAsync();
                await LoadCategories();
                await DisplayAlert("Success", "Category updated successfully", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to edit category: {ex.Message}", "OK");
            }
        }

        private async Task DeleteCategory(Category category)
        {
            bool confirm = await DisplayAlert("Confirm Delete", $"Are you sure you want to delete '{category.Name}'?", "Yes", "No");
            if (!confirm) return;

            try
            {
                using var connection = new MySqlConnection(ConnectionString);
                await connection.OpenAsync();

                string query = "DELETE FROM document_categories WHERE id = @id";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", category.Id);

                await command.ExecuteNonQueryAsync();
                await LoadCategories();
                await DisplayAlert("Success", "Category deleted successfully", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to delete category: {ex.Message}", "OK");
            }
        }

        //Commands
        public ICommand CreateCategoryCommand => new Command(async () => await CreateCategory());
        public ICommand EditCategoryCommand => new Command<Category>(async (cat) => await EditCategory(cat));
        public ICommand DeleteCategoryCommand => new Command<Category>(async (cat) => await DeleteCategory(cat));
        public ICommand AddUserCommand => new Command(async () => await AddUser());
        public ICommand EditUserCommand => new Command<User>(async (user) => await EditUser(user));
        public ICommand DeleteUserCommand => new Command<User>(async (user) => await DeleteUser(user));
        public ICommand AddDocumentCommand => new Command(async () => await AddDocument());
        public ICommand EditDocumentCommand => new Command<Document>(async (doc) => await EditDocument(doc));
        public ICommand DeleteDocumentCommand => new Command<Document>(async (doc) => await DeleteDocument(doc));
        public ICommand RefreshCommand => new Command(() => LoadData());



        //Create Category
        private async Task CreateCategory()
        {
            try
            {
                var categoryName = await DisplayPromptAsync("Create Category", "Enter category name:");
                if (string.IsNullOrWhiteSpace(categoryName)) return;

                using var conn = new MySqlConnection(ConnectionString);
                await conn.OpenAsync();

                var insertQuery = "INSERT INTO document_categories (name) VALUES (@name)";
                using var insertCmd = new MySqlCommand(insertQuery, conn);
                insertCmd.Parameters.AddWithValue("@name", categoryName);
                await insertCmd.ExecuteNonQueryAsync();

                long insertedId = insertCmd.LastInsertedId;

                Categories.Add(new Category
                {
                    Id = (int)insertedId,
                    Name = categoryName
                });

                await DisplayAlert("Success", "Category created successfully", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }


        //Add User
        private async Task AddUser()
        {
            try
            {
                var username = await DisplayPromptAsync("Add User", "Username:");
                if (string.IsNullOrWhiteSpace(username)) return;

                var email = await DisplayPromptAsync("Add User", "Email:");
                if (string.IsNullOrWhiteSpace(email)) return;

                var password = await DisplayPromptAsync("Add User", "Password:", initialValue: "", keyboard: Keyboard.Text);
                if (string.IsNullOrWhiteSpace(password)) return;

                var role = await DisplayActionSheet("Select Role", "Cancel", null, "Admin", "User");
                if (role == "Cancel") return;

                // Hash password with bcrypt
                string hashedPassword = BCrypt.Net.BCrypt.HashPassword(password);

                using var connection = new MySqlConnection(ConnectionString);
                await connection.OpenAsync();

                var query = "INSERT INTO users (username, email, password, role) VALUES (@username, @email, @password, @role)";
                using var command = new MySqlCommand(query, connection);

                command.Parameters.AddWithValue("@username", username);
                command.Parameters.AddWithValue("@email", email);
                command.Parameters.AddWithValue("@password", hashedPassword);
                command.Parameters.AddWithValue("@role", role);

                await command.ExecuteNonQueryAsync();
                await LoadUsers();
                await DisplayAlert("Success", "User added successfully", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to add user: {ex.Message}", "OK");
            }
        }        //Edit User
        private async Task EditUser(User user)
        {
            try
            {
                var newUsername = await DisplayPromptAsync("Edit User", "Username:", initialValue: user.Username);
                if (string.IsNullOrWhiteSpace(newUsername)) return;

                var newRole = await DisplayActionSheet("Select Role", "Cancel", null, "Admin", "User");
                if (newRole == "Cancel") return;

                // Optional: Prompt for new password
                var newPassword = await DisplayPromptAsync("Edit User", "New Password (leave empty to keep current)", placeholder: "Leave blank to skip", maxLength: 100, keyboard: Keyboard.Text, initialValue: "");

                string updateQuery;
                using var connection = new MySqlConnection(ConnectionString);
                await connection.OpenAsync();

                if (!string.IsNullOrWhiteSpace(newPassword))
                {
                    if (newPassword.Length < 6)
                    {
                        await DisplayAlert("Error", "Password must be at least 6 characters.", "OK");
                        return;
                    }

                    // Hash the new password
                    string hashedPassword = BCrypt.Net.BCrypt.HashPassword(newPassword);

                    updateQuery = "UPDATE users SET username = @username, email = @email, role = @role, password = @password WHERE id = @id";

                    using var command = new MySqlCommand(updateQuery, connection);
                    command.Parameters.AddWithValue("@username", newUsername);
                    command.Parameters.AddWithValue("@role", newRole);
                    command.Parameters.AddWithValue("@password", hashedPassword);
                    command.Parameters.AddWithValue("@id", user.Id);

                    await command.ExecuteNonQueryAsync();
                }
                else
                {
                    updateQuery = "UPDATE users SET username = @username, email = @email, role = @role WHERE id = @id";

                    using var command = new MySqlCommand(updateQuery, connection);
                    command.Parameters.AddWithValue("@username", newUsername);
                    command.Parameters.AddWithValue("@role", newRole);
                    command.Parameters.AddWithValue("@id", user.Id);

                    await command.ExecuteNonQueryAsync();
                }

                await LoadUsers();
                await DisplayAlert("Success", "User updated successfully", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to edit user: {ex.Message}", "OK");
            }
        }

        //delete user
        private async Task<bool> DeleteUser(User user)
        {
            bool confirm = await DisplayAlert("Confirm Delete",
                $"Are you sure you want to delete {user.Email} and all their activity logs?",
                "Yes", "No");
            if (!confirm)
                return false;

            try
            {
                using MySqlConnection conn = new MySqlConnection(ConnectionString);
                await conn.OpenAsync();

                using var transaction = await conn.BeginTransactionAsync();

                try
                {
                    // First delete activity logs
                    string deleteLogsQuery = "DELETE FROM activity_logs WHERE user_id = @UserId";
                    using MySqlCommand deleteLogsCmd = new MySqlCommand(deleteLogsQuery, conn, transaction);
                    deleteLogsCmd.Parameters.AddWithValue("@UserId", user.Id);
                    await deleteLogsCmd.ExecuteNonQueryAsync();

                    // Then delete user
                    string deleteUserQuery = "DELETE FROM users WHERE id = @Id";
                    using MySqlCommand deleteUserCmd = new MySqlCommand(deleteUserQuery, conn, transaction);
                    deleteUserCmd.Parameters.AddWithValue("@Id", user.Id);

                    int rowsAffected = await deleteUserCmd.ExecuteNonQueryAsync();

                    if (rowsAffected > 0)
                    {
                        await transaction.CommitAsync();
                        Users.Remove(user);
                        await DisplayAlert("Success", "User and their activity logs deleted successfully.", "OK");
                        return true;
                    }
                    else
                    {
                        await transaction.RollbackAsync();
                        await DisplayAlert("Failed", "User not found or already deleted.", "OK");
                        return false;
                    }
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
                return false;
            }
        }

        //Add Document
        private async Task AddDocument()
        {
            try
            {
                var fileTypes = new FilePickerFileType(
                    new Dictionary<DevicePlatform, IEnumerable<string>>
                    {
                { DevicePlatform.WinUI, new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".pptx" } },
                { DevicePlatform.MacCatalyst, new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".pptx" } },
                { DevicePlatform.Android, new[]
                    {
                        "application/pdf",
                        "application/msword",
                        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                        "application/vnd.ms-excel",
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        "application/vnd.openxmlformats-officedocument.presentationml.presentation"
                    }
                }
             });

                var result = await FilePicker.PickAsync(new PickOptions
                {
                    PickerTitle = "Please select a document",
                    FileTypes = fileTypes
                });

                if (result == null) return;

                var _selectedFileName = result.FileName;
                byte[] _selectedFileBytes;

                using (var stream = await result.OpenReadAsync())
                using (var ms = new MemoryStream())
                {
                    await stream.CopyToAsync(ms);
                    _selectedFileBytes = ms.ToArray();
                }

                const int maxFileSizeBytes = 32 * 1024 * 1024;
                if (_selectedFileBytes.Length > maxFileSizeBytes)
                {
                    await DisplayAlert("File Too Large", "The selected document exceeds the 32MB limit.", "OK");
                    return;
                }
                var categories = new List<string>();
                using (var connection = new MySqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();
                    using var command = new MySqlCommand("SELECT name FROM document_categories", connection);
                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        categories.Add(reader.GetString("name"));
                    }
                }

                var category = await DisplayActionSheet("Select Category", "Cancel", null, categories.ToArray());
                if (category == "Cancel") return;

                string title = await DisplayPromptAsync("Document Title", "Enter document title:");
                if (string.IsNullOrWhiteSpace(title)) return;

                using var connection2 = new MySqlConnection(ConnectionString);
                await connection2.OpenAsync();

                var query = "INSERT INTO documents (title, file_name, file_content, upload_date, category_id) " +
                            "VALUES (@title, @fileName, @fileContent, @uploadDate, " +
                            "(SELECT id FROM document_categories WHERE name = @category))";

                using var command2 = new MySqlCommand(query, connection2);
                command2.Parameters.AddWithValue("@title", title);
                command2.Parameters.AddWithValue("@fileName", _selectedFileName);
                command2.Parameters.AddWithValue("@fileContent", _selectedFileBytes);
                command2.Parameters.AddWithValue("@uploadDate", DateTime.Now);
                command2.Parameters.AddWithValue("@category", category);

                await command2.ExecuteNonQueryAsync();
                await LoadDocuments();
                await DisplayAlert("Success", "Document added successfully", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to add document: {ex.Message}", "OK");
            }
        }

        private async void OnDocumentSelected(object sender, SelectionChangedEventArgs e)
        {
            var selectedDocument = e.CurrentSelection.FirstOrDefault() as Document;
            if (selectedDocument == null) return;

            var open = await DisplayAlert("Document Selected",
    $"Title: {selectedDocument.Title}\n" +
    $"Category: {selectedDocument.CategoryName}\n" +
    $"Uploaded By: {selectedDocument.UploaderName}\n" +
    $"Upload Date: {selectedDocument.UploadDate:MMM dd, yyyy}\n\n" +
    "Open this document?",
    "Open", "Cancel");

            if (open)
            {
                await OpenDocumentAsync(selectedDocument.FileBytes, selectedDocument.FileName);
            }

              ((CollectionView)sender).SelectedItem = null;
        }

        private async Task OpenDocumentAsync(byte[] fileBytes, string fileName)
        {
            try
            {
                if (fileBytes == null || fileBytes.Length == 0)
                {
                    await DisplayAlert("Error", "The document has no content.", "OK");
                    return;
                }

                string extension = Path.GetExtension(fileName)?.ToLower();
                string localFilePath = Path.Combine(FileSystem.CacheDirectory, fileName);

                //Save file to cache directory
                await File.WriteAllBytesAsync(localFilePath, fileBytes);

                //Supported document types
                string[] supportedExtensions = new[] { ".pdf", ".doc", ".docx", ".xlsx", ".pptx" };

                if (supportedExtensions.Contains(extension))
                {
                    await Launcher.OpenAsync(new OpenFileRequest
                    {
                        File = new ReadOnlyFile(localFilePath)
                    });
                }
                else
                {
                    await DisplayAlert("Unsupported File Type", $"The file type '{extension}' is not supported for preview.", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Failed to Open", $"An error occurred while opening the file:\n{ex.Message}", "OK");
            }
        }

        //Edit Document
        private async Task EditDocument(Document doc)
        {
            try
            {
                var newTitle = await DisplayPromptAsync("Edit Document", "Title:", initialValue: doc.Title);
                if (string.IsNullOrWhiteSpace(newTitle)) return;

                var categories = new List<string>();
                using (var connection = new MySqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();
                    using var command = new MySqlCommand("SELECT name FROM document_categories", connection);
                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        categories.Add(reader.GetString("name"));
                    }
                }

                var newCategory = await DisplayActionSheet("Select Category", "Cancel", null, categories.ToArray());
                if (newCategory == "Cancel") return;

                using var connection2 = new MySqlConnection(ConnectionString);
                await connection2.OpenAsync();

                var query = "UPDATE documents SET title = @title, category_id = " +
                           "(SELECT id FROM document_categories WHERE name = @category) " +
                           "WHERE id = @id";
                using var command2 = new MySqlCommand(query, connection2);

                command2.Parameters.AddWithValue("@title", newTitle);
                command2.Parameters.AddWithValue("@category", newCategory);
                command2.Parameters.AddWithValue("@id", doc.Id);

                await command2.ExecuteNonQueryAsync();
                await LoadDocuments();
                await DisplayAlert("Success", "Document updated successfully", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to edit document: {ex.Message}", "OK");
            }
        }

        //Delete Document
        private async Task DeleteDocument(Document doc)
        {
            try
            {
                bool confirm = await DisplayAlert("Confirm", $"Delete document '{doc.Title}'?", "Yes", "No");
                if (!confirm) return;

                using var connection = new MySqlConnection(ConnectionString);
                await connection.OpenAsync();

                var query = "DELETE FROM documents WHERE id = @id";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", doc.Id);

                await command.ExecuteNonQueryAsync();
                await LoadDocuments();
                await DisplayAlert("Success", "Document deleted successfully", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to delete document: {ex.Message}", "OK");
            }
        }

        public ICommand SendStorageAlertCommand => new Command(async () =>
        {
            var usersNearLimit = StorageUsageByUser.Where(u => u.Percentage > 15).ToList();

            if (usersNearLimit.Any())
            {
                var userList = string.Join("\n", usersNearLimit.Select(u => $"- {u.Username} ({u.Percentage:0}%)"));
                await DisplayAlert("Storage Alerts",
                    $"The following users are nearing storage limits:\n{userList}",
                    "OK");
            }
            else
            {
                await DisplayAlert("Storage Status", "No users are currently nearing storage limits.", "OK");
            }
        });

        public ICommand ViewStorageReportCommand => new Command(async () =>
        {
            var report = new StringBuilder();
            report.AppendLine("Storage Usage Report");
            report.AppendLine("====================");
            report.AppendLine($"Total Usage: {StorageUsageText} ({StorageUsagePercentage:P0})");
            report.AppendLine("\nBy User:");

            foreach (var user in StorageUsageByUser.OrderByDescending(u => u.StorageUsed))
            {
                report.AppendLine($"{user.Username}: {user.StorageUsed:0.0} MB ({user.Percentage:0.0}%)");
            }

            await DisplayAlert("Storage Report", report.ToString(), "OK");
        });

        //Property Changed Event
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // New Model for Storage Usage
    public class StorageUsage
        {
            public string Username { get; set; }
            public double StorageUsed { get; set; } // in MB
            public double Percentage { get; set; }
        }
    //User Model
    public class User
    {
        public int Id { get; set; }
        public string? Username { get; set; }
        public string? Email { get; set; }
        public string? Role { get; set; }
    }

    // Document Model
    public class Document
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public string? FileName { get; set; }
        public string? CategoryName { get; set; }
        public byte[]? FileBytes { get; set; }
        public DateTime UploadDate { get; set; }
        public string? PreviewImage { get; set; }
        public string UploaderName { get; set; }
    }
    public class Category
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}