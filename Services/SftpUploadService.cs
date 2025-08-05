using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using Systems_One_Sftp_Upload_Service.Models;

namespace Systems_One_Sftp_Upload_Service.Services
{
    /// <summary>
    /// Service for uploading files via SFTP
    /// </summary>
    public class SftpUploadService : IDisposable
    {
        private readonly ILogger<SftpUploadService> _logger;
        private readonly AppSettings _settings;
        private SftpClient? _sftpClient;
        private bool _disposed;

        public SftpUploadService(ILogger<SftpUploadService> logger, AppSettings settings)
        {
            _logger = logger;
            _settings = settings;
        }

        /// <summary>
        /// Tests the SFTP connection with current settings
        /// </summary>
        /// <returns>True if connection successful, false otherwise</returns>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                _logger.LogInformation("Testing SFTP connection...");
                
                var connectionInfo = GetConnectionInfo();
                if (connectionInfo == null)
                {
                    _logger.LogError("Cannot test connection: SFTP settings are not configured");
                    return false;
                }

                using var testClient = new SftpClient(connectionInfo);
                
                await Task.Run(() =>
                {
                    testClient.Connect();
                    
                    if (testClient.IsConnected)
                    {
                        _logger.LogInformation("SFTP connection test successful");
                        
                        // Test remote directory access with comprehensive logging
                        return TestRemoteDirectoryAccess(testClient);
                    }
                    
                    _logger.LogError("SFTP connection failed - client not connected");
                    return false;
                });

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SFTP connection test failed");
                return false;
            }
        }

        /// <summary>
        /// Tests access to the remote directory with detailed logging
        /// </summary>
        /// <param name="client">Connected SFTP client</param>
        /// <returns>True if directory is accessible</returns>
        private bool TestRemoteDirectoryAccess(SftpClient client)
        {
            try
            {
                var remoteDir = _settings.Sftp?.RemoteDirectory ?? "/";
                _logger.LogInformation("Testing access to remote directory: '{RemoteDir}'", remoteDir);
                
                // Check if the directory exists
                if (!client.Exists(remoteDir))
                {
                    _logger.LogError("Remote directory does not exist: '{RemoteDir}'", remoteDir);
                    _logger.LogError("Please verify the RemoteDirectory setting in your configuration");
                    return false;
                }
                
                // Try to list directory contents
                var items = client.ListDirectory(remoteDir).Take(10).ToList();
                _logger.LogInformation("Remote directory '{RemoteDir}' is accessible, found {ItemCount} items", 
                    remoteDir, items.Count);
                
                // Log directory contents for debugging
                if (items.Count > 0)
                {
                    _logger.LogDebug("Directory contents:");
                    foreach (var item in items.Take(5))
                    {
                        var itemType = item.IsDirectory ? "DIR" : item.IsRegularFile ? "FILE" : "OTHER";
                        _logger.LogDebug("  [{Type}] {Name} ({Size} bytes, {ModTime})", 
                            itemType, item.Name, item.Length, item.LastWriteTime);
                    }
                    
                    if (items.Count > 5)
                    {
                        _logger.LogDebug("  ... and {RemainingCount} more items", items.Count - 5);
                    }
                }
                else
                {
                    _logger.LogInformation("Remote directory is empty (no files or subdirectories)");
                }
                
                // Test write permissions by checking if we can create a temporary directory
                if (!TestWritePermissions(client, remoteDir))
                {
                    _logger.LogWarning("Write permission test failed for remote directory: '{RemoteDir}'", remoteDir);
                    _logger.LogWarning("File uploads may fail due to insufficient permissions");
                }
                
                client.Disconnect();
                return true;
            }
            catch (Renci.SshNet.Common.SftpPermissionDeniedException ex)
            {
                _logger.LogError(ex, "Permission denied accessing remote directory: '{RemoteDir}'", 
                    _settings.Sftp?.RemoteDirectory ?? "/");
                _logger.LogError("Check that the SFTP user has read/write permissions to the directory");
                return false;
            }
            catch (Renci.SshNet.Common.SftpPathNotFoundException ex)
            {
                _logger.LogError(ex, "Remote directory path not found: '{RemoteDir}'", 
                    _settings.Sftp?.RemoteDirectory ?? "/");
                _logger.LogError("Please verify the RemoteDirectory path exists on the server");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing remote directory access: '{RemoteDir}'", 
                    _settings.Sftp?.RemoteDirectory ?? "/");
                return false;
            }
        }

        /// <summary>
        /// Tests write permissions to the remote directory
        /// </summary>
        /// <param name="client">Connected SFTP client</param>
        /// <param name="remoteDir">Remote directory path</param>
        /// <returns>True if write permissions are available</returns>
        private bool TestWritePermissions(SftpClient client, string remoteDir)
        {
            try
            {
                var testFileName = $".write_test_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}";
                var testFilePath = CombinePath(remoteDir, testFileName);
                
                _logger.LogDebug("Testing write permissions with temporary file: {TestFile}", testFilePath);
                
                // Create a small test file
                using var testStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("write test"));
                client.UploadFile(testStream, testFilePath);
                
                // Verify the file was created
                if (client.Exists(testFilePath))
                {
                    // Clean up the test file
                    client.DeleteFile(testFilePath);
                    _logger.LogDebug("Write permission test successful - test file created and deleted");
                    return true;
                }
                else
                {
                    _logger.LogWarning("Write permission test failed - test file was not created");
                    return false;
                }
            }
            catch (Renci.SshNet.Common.SftpPermissionDeniedException ex)
            {
                _logger.LogWarning(ex, "Write permission denied in remote directory");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Write permission test failed with error");
                return false;
            }
        }

        /// <summary>
        /// Connects to the SFTP server
        /// </summary>
        /// <returns>True if connection successful</returns>
        public async Task<bool> ConnectAsync()
        {
            try
            {
                if (_sftpClient?.IsConnected == true)
                {
                    _logger.LogDebug("Already connected to SFTP server");
                    return true;
                }

                var connectionInfo = GetConnectionInfo();
                if (connectionInfo == null)
                {
                    _logger.LogError("Cannot connect: SFTP settings are not configured");
                    return false;
                }

                _logger.LogInformation("Connecting to SFTP server: {Host}:{Port}", 
                    _settings.Sftp?.Host, _settings.Sftp?.GetPort());

                _sftpClient = new SftpClient(connectionInfo);
                
                await Task.Run(() => _sftpClient.Connect());
                
                if (_sftpClient.IsConnected)
                {
                    _logger.LogInformation("Successfully connected to SFTP server {Host}:{Port}", 
                        _settings.Sftp?.Host, _settings.Sftp?.GetPort());
                    
                    // Immediately validate the remote directory after connection
                    var remoteDir = _settings.Sftp?.RemoteDirectory ?? "/";
                    _logger.LogInformation("Validating remote directory after connection: {RemoteDir}", remoteDir);
                    
                    if (!await ValidateRemoteDirectoryAsync(remoteDir))
                    {
                        _logger.LogWarning("Connected to SFTP server but remote directory validation failed");
                        _logger.LogWarning("File uploads may fail due to directory access issues");
                    }
                    else
                    {
                        _logger.LogInformation("Remote directory validation successful");
                    }
                    
                    return true;
                }
                else
                {
                    _logger.LogError("Failed to connect to SFTP server");
                    return false;
                }
            }
            catch (Renci.SshNet.Common.SshConnectionException ex)
            {
                _logger.LogError(ex, "SSH connection failed to {Host}:{Port}", 
                    _settings.Sftp?.Host, _settings.Sftp?.GetPort());
                _logger.LogError("Check network connectivity and server availability");
                return false;
            }
            catch (Renci.SshNet.Common.SshAuthenticationException ex)
            {
                _logger.LogError(ex, "SFTP authentication failed for user: {Username}", 
                    _settings.Sftp?.Username);
                _logger.LogError("Check username and password in SFTP settings");
                return false;
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                _logger.LogError(ex, "Network error connecting to SFTP server {Host}:{Port}", 
                    _settings.Sftp?.Host, _settings.Sftp?.GetPort());
                _logger.LogError("Check network connectivity and firewall settings");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error connecting to SFTP server");
                return false;
            }
        }

        /// <summary>
        /// Disconnects from the SFTP server
        /// </summary>
        public void Disconnect()
        {
            try
            {
                if (_sftpClient?.IsConnected == true)
                {
                    _sftpClient.Disconnect();
                    _logger.LogInformation("Disconnected from SFTP server");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during SFTP disconnect");
            }
        }

        /// <summary>
        /// Uploads a single file to the SFTP server
        /// </summary>
        /// <param name="localFilePath">Local file path</param>
        /// <param name="remoteFileName">Optional remote filename (uses local filename if null)</param>
        /// <returns>True if upload successful</returns>
        public async Task<bool> UploadFileAsync(string localFilePath, string? remoteFileName = null)
        {
            try
            {
                if (!File.Exists(localFilePath))
                {
                    _logger.LogError("Local file not found: {LocalFilePath}", localFilePath);
                    return false;
                }

                if (!await EnsureConnectedAsync())
                {
                    _logger.LogError("Cannot upload file - SFTP connection failed");
                    return false;
                }

                var fileName = remoteFileName ?? Path.GetFileName(localFilePath);
                var remoteDir = _settings.Sftp?.RemoteDirectory ?? "/";
                var remotePath = CombinePath(remoteDir, fileName);

                // Validate remote directory accessibility before upload
                if (!await ValidateRemoteDirectoryAsync(remoteDir))
                {
                    _logger.LogError("Cannot upload file - remote directory validation failed: {RemoteDir}", remoteDir);
                    return false;
                }

                var fileInfo = new FileInfo(localFilePath);
                _logger.LogInformation("Uploading file: {LocalFile} ? {RemotePath} ({Size} bytes)", 
                    localFilePath, remotePath, fileInfo.Length);

                using var fileStream = File.OpenRead(localFilePath);
                
                await Task.Run(() => _sftpClient!.UploadFile(fileStream, remotePath));
                
                // Verify upload by checking if file exists on remote
                var uploadedFileExists = await Task.Run(() => _sftpClient!.Exists(remotePath));
                
                if (uploadedFileExists)
                {
                    _logger.LogInformation("Successfully uploaded file: {FileName}", fileName);
                    
                    // Log additional verification info
                    try
                    {
                        var remoteFileInfo = await Task.Run(() => _sftpClient!.GetAttributes(remotePath));
                        _logger.LogDebug("Remote file verification: {RemoteFile} ({Size} bytes, {ModTime})", 
                            remotePath, remoteFileInfo.Size, remoteFileInfo.LastWriteTime);
                        
                        if (remoteFileInfo.Size != fileInfo.Length)
                        {
                            _logger.LogWarning("File size mismatch: local={LocalSize}, remote={RemoteSize}", 
                                fileInfo.Length, remoteFileInfo.Size);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not verify remote file attributes");
                    }
                    
                    return true;
                }
                else
                {
                    _logger.LogError("Upload verification failed: remote file not found after upload");
                    return false;
                }
            }
            catch (Renci.SshNet.Common.SftpPermissionDeniedException ex)
            {
                _logger.LogError(ex, "Permission denied uploading file: {LocalFilePath} ? {RemotePath}", 
                    localFilePath, CombinePath(_settings.Sftp?.RemoteDirectory ?? "/", remoteFileName ?? Path.GetFileName(localFilePath)));
                _logger.LogError("Check SFTP user permissions for the remote directory");
                return false;
            }
            catch (Renci.SshNet.Common.SftpPathNotFoundException ex)
            {
                _logger.LogError(ex, "Remote path not found during upload: {LocalFilePath} ? {RemotePath}", 
                    localFilePath, CombinePath(_settings.Sftp?.RemoteDirectory ?? "/", remoteFileName ?? Path.GetFileName(localFilePath)));
                _logger.LogError("Verify that the remote directory exists and is accessible");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file: {LocalFilePath}", localFilePath);
                return false;
            }
        }

        /// <summary>
        /// Validates that the remote directory is accessible and writable
        /// </summary>
        /// <param name="remoteDir">Remote directory path to validate</param>
        /// <returns>True if directory is accessible and writable</returns>
        private async Task<bool> ValidateRemoteDirectoryAsync(string remoteDir)
        {
            try
            {
                if (_sftpClient?.IsConnected != true)
                {
                    _logger.LogError("Cannot validate remote directory - SFTP client not connected");
                    return false;
                }

                _logger.LogDebug("Validating remote directory: {RemoteDir}", remoteDir);

                // Try to check if directory exists - this may throw permission exception
                bool directoryExists;
                bool isDirectory = false;
                
                try
                {
                    directoryExists = await Task.Run(() => _sftpClient.Exists(remoteDir));
                    
                    if (directoryExists)
                    {
                        // Check if it's actually a directory
                        var attributes = await Task.Run(() => _sftpClient.GetAttributes(remoteDir));
                        isDirectory = attributes.IsDirectory;
                        
                        if (!isDirectory)
                        {
                            _logger.LogError("Remote path exists but is not a directory: {RemoteDir}", remoteDir);
                            _logger.LogError("Please specify a valid directory path in the RemoteDirectory setting");
                            return false;
                        }
                    }
                }
                catch (Renci.SshNet.Common.SftpPermissionDeniedException ex)
                {
                    _logger.LogWarning(ex, "Permission denied when checking directory existence: {RemoteDir}", remoteDir);
                    _logger.LogInformation("Directory may exist but user lacks read permissions to verify");
                    _logger.LogInformation("Attempting alternative validation method...");
                    
                    // Try an alternative approach - attempt to list the directory
                    // If this works, the directory exists and we have some access
                    try
                    {
                        var items = await Task.Run(() => _sftpClient.ListDirectory(remoteDir).Take(1).ToList());
                        _logger.LogInformation("? Directory appears to exist (can list contents): {RemoteDir}", remoteDir);
                        directoryExists = true;
                        isDirectory = true;
                    }
                    catch (Renci.SshNet.Common.SftpPermissionDeniedException listEx)
                    {
                        _logger.LogError(listEx, "Permission denied listing directory contents: {RemoteDir}", remoteDir);
                        _logger.LogError("Check that the SFTP user has appropriate permissions:");
                        _logger.LogError("  1. Read permission to check if directory exists");
                        _logger.LogError("  2. List permission to view directory contents");
                        _logger.LogError("  3. Write permission to upload files");
                        return false;
                    }
                    catch (Renci.SshNet.Common.SftpPathNotFoundException listEx)
                    {
                        _logger.LogError(listEx, "Directory does not exist: {RemoteDir}", remoteDir);
                        _logger.LogError("Please verify the RemoteDirectory setting points to a valid path on the server");
                        return false;
                    }
                    catch (Exception listEx)
                    {
                        _logger.LogError(listEx, "Error during alternative directory validation: {RemoteDir}", remoteDir);
                        return false;
                    }
                }
                
                if (!directoryExists)
                {
                    _logger.LogError("Remote directory does not exist: {RemoteDir}", remoteDir);
                    _logger.LogError("Please ensure the directory exists on the SFTP server or update the RemoteDirectory setting");
                    
                    // Provide helpful suggestions
                    _logger.LogError("Troubleshooting suggestions:");
                    _logger.LogError("  1. Verify the path is correct: '{RemoteDir}'", remoteDir);
                    _logger.LogError("  2. Check if the directory exists on the server");
                    _logger.LogError("  3. Ensure you have permission to access the directory");
                    _logger.LogError("  4. Try using an absolute path (starting with /)");
                    
                    return false;
                }

                _logger.LogDebug("Remote directory validation successful: {RemoteDir}", remoteDir);
                return true;
            }
            catch (Renci.SshNet.Common.SftpPathNotFoundException ex)
            {
                _logger.LogError(ex, "Remote directory path not found: {RemoteDir}", remoteDir);
                _logger.LogError("Please verify the RemoteDirectory setting points to a valid path on the server");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating remote directory: {RemoteDir}", remoteDir);
                return false;
            }
        }

        /// <summary>
        /// Uploads multiple files to the SFTP server
        /// </summary>
        /// <param name="localFilePaths">Array of local file paths</param>
        /// <returns>Array of upload results (true for successful uploads)</returns>
        public async Task<bool[]> UploadFilesAsync(string[] localFilePaths)
        {
            var results = new bool[localFilePaths.Length];
            
            if (localFilePaths.Length == 0)
            {
                _logger.LogInformation("No files to upload");
                return results;
            }

            _logger.LogInformation("Starting upload of {FileCount} files", localFilePaths.Length);

            if (!await EnsureConnectedAsync())
            {
                _logger.LogError("Cannot upload files: SFTP connection failed");
                return results;
            }

            for (int i = 0; i < localFilePaths.Length; i++)
            {
                var filePath = localFilePaths[i];
                var fileName = Path.GetFileName(filePath);
                
                _logger.LogInformation("Uploading file {Current}/{Total}: {FileName}", 
                    i + 1, localFilePaths.Length, fileName);

                results[i] = await UploadFileAsync(filePath);
                
                if (results[i])
                {
                    _logger.LogInformation("? Upload successful: {FileName}", fileName);
                }
                else
                {
                    _logger.LogError("? Upload failed: {FileName}", fileName);
                }

                // Small delay between uploads to avoid overwhelming the server
                await Task.Delay(100);
            }

            var successCount = results.Count(r => r);
            _logger.LogInformation("Upload batch completed: {SuccessCount}/{TotalCount} files uploaded successfully", 
                successCount, localFilePaths.Length);

            return results;
        }

        /// <summary>
        /// Uploads files with retry logic
        /// </summary>
        /// <param name="localFilePaths">Array of local file paths</param>
        /// <param name="maxRetries">Maximum number of retry attempts per file</param>
        /// <param name="retryDelayMs">Delay between retry attempts in milliseconds</param>
        /// <returns>Array of upload results</returns>
        public async Task<bool[]> UploadFilesWithRetryAsync(string[] localFilePaths, int maxRetries = 3, int retryDelayMs = 1000)
        {
            var results = new bool[localFilePaths.Length];
            
            if (localFilePaths.Length == 0)
            {
                return results;
            }

            _logger.LogInformation("Starting upload with retry logic: {FileCount} files, max {MaxRetries} retries", 
                localFilePaths.Length, maxRetries);

            for (int i = 0; i < localFilePaths.Length; i++)
            {
                var filePath = localFilePaths[i];
                var fileName = Path.GetFileName(filePath);
                
                for (int attempt = 1; attempt <= maxRetries + 1; attempt++)
                {
                    _logger.LogInformation("Uploading {FileName} (attempt {Attempt}/{MaxAttempts})", 
                        fileName, attempt, maxRetries + 1);

                    results[i] = await UploadFileAsync(filePath);
                    
                    if (results[i])
                    {
                        _logger.LogInformation("? Upload successful: {FileName}", fileName);
                        break;
                    }
                    else if (attempt <= maxRetries)
                    {
                        _logger.LogWarning("Upload failed, retrying in {DelayMs}ms: {FileName}", 
                            retryDelayMs, fileName);
                        
                        // Disconnect and reconnect on failure to ensure clean connection
                        Disconnect();
                        
                        await Task.Delay(retryDelayMs);
                        
                        // Exponential backoff for retry delay
                        retryDelayMs = Math.Min(retryDelayMs * 2, 30000); // Cap at 30 seconds
                    }
                    else
                    {
                        _logger.LogError("? Upload failed after {MaxRetries} retries: {FileName}", 
                            maxRetries, fileName);
                    }
                }
            }

            var successCount = results.Count(r => r);
            _logger.LogInformation("Upload with retry completed: {SuccessCount}/{TotalCount} files uploaded successfully", 
                successCount, localFilePaths.Length);

            return results;
        }

        /// <summary>
        /// Lists files in the remote directory
        /// </summary>
        /// <param name="remotePath">Remote directory path (null for default)</param>
        /// <returns>Array of remote file information</returns>
        public async Task<ISftpFile[]> ListRemoteFilesAsync(string? remotePath = null)
        {
            try
            {
                if (!await EnsureConnectedAsync())
                {
                    _logger.LogError("Cannot list remote files - SFTP connection failed");
                    return Array.Empty<ISftpFile>();
                }

                var remoteDir = remotePath ?? _settings.Sftp?.RemoteDirectory ?? "/";
                
                _logger.LogDebug("Listing files in remote directory: {RemoteDir}", remoteDir);

                // Validate directory access before listing
                if (!await ValidateRemoteDirectoryAsync(remoteDir))
                {
                    _logger.LogError("Cannot list files - remote directory validation failed: {RemoteDir}", remoteDir);
                    return Array.Empty<ISftpFile>();
                }
                
                var files = await Task.Run(() => 
                    _sftpClient!.ListDirectory(remoteDir)
                        .Where(f => f.IsRegularFile)
                        .OrderByDescending(f => f.LastWriteTime)
                        .ToArray());

                _logger.LogInformation("Found {FileCount} files in remote directory: {RemoteDir}", 
                    files.Length, remoteDir);

                // Log some file details for debugging
                if (files.Length > 0)
                {
                    _logger.LogDebug("Recent files in remote directory:");
                    foreach (var file in files.Take(3))
                    {
                        _logger.LogDebug("  - {FileName} ({Size} bytes, {ModTime})", 
                            file.Name, file.Length, file.LastWriteTime);
                    }
                    
                    if (files.Length > 3)
                    {
                        _logger.LogDebug("  ... and {RemainingCount} more files", files.Length - 3);
                    }
                }

                return files;
            }
            catch (Renci.SshNet.Common.SftpPermissionDeniedException ex)
            {
                _logger.LogError(ex, "Permission denied listing remote directory: {RemoteDir}", 
                    remotePath ?? _settings.Sftp?.RemoteDirectory ?? "/");
                _logger.LogError("Check that the SFTP user has read permissions for this directory");
                return Array.Empty<ISftpFile>();
            }
            catch (Renci.SshNet.Common.SftpPathNotFoundException ex)
            {
                _logger.LogError(ex, "Remote directory not found: {RemoteDir}", 
                    remotePath ?? _settings.Sftp?.RemoteDirectory ?? "/");
                _logger.LogError("Please verify the directory path exists on the server");
                return Array.Empty<ISftpFile>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing remote files in directory: {RemoteDir}", 
                    remotePath ?? _settings.Sftp?.RemoteDirectory ?? "/");
                return Array.Empty<ISftpFile>();
            }
        }

        /// <summary>
        /// Gets SFTP connection information from settings
        /// </summary>
        /// <returns>ConnectionInfo object or null if settings are invalid</returns>
        private ConnectionInfo? GetConnectionInfo()
        {
            var sftpSettings = _settings.Sftp;
            
            if (string.IsNullOrEmpty(sftpSettings?.Host) || 
                string.IsNullOrEmpty(sftpSettings?.Username) || 
                string.IsNullOrEmpty(sftpSettings?.Password))
            {
                _logger.LogError("SFTP settings are incomplete. Host, Username, and Password are required");
                return null;
            }

            var host = sftpSettings.Host;
            var port = sftpSettings.GetPort();
            var username = sftpSettings.Username;
            var password = sftpSettings.Password;

            _logger.LogDebug("Creating SFTP connection to {Host}:{Port} as user {Username}", 
                host, port, username);

            return new ConnectionInfo(host, port, username,
                new PasswordAuthenticationMethod(username, password));
        }

        /// <summary>
        /// Ensures the SFTP client is connected
        /// </summary>
        /// <returns>True if connected or successfully connected</returns>
        private async Task<bool> EnsureConnectedAsync()
        {
            if (_sftpClient?.IsConnected == true)
            {
                return true;
            }

            return await ConnectAsync();
        }

        /// <summary>
        /// Validates SFTP settings
        /// </summary>
        /// <returns>Validation result with details</returns>
        public (bool IsValid, string[] Issues) ValidateSettings()
        {
            var issues = new List<string>();
            
            if (_settings.Sftp == null)
            {
                issues.Add("SFTP settings section is missing");
            }
            else
            {
                if (string.IsNullOrEmpty(_settings.Sftp.Host))
                    issues.Add("SFTP Host is required");
                
                if (string.IsNullOrEmpty(_settings.Sftp.Username))
                    issues.Add("SFTP Username is required");
                
                if (string.IsNullOrEmpty(_settings.Sftp.Password))
                    issues.Add("SFTP Password is required");
                
                var port = _settings.Sftp.GetPort();
                if (port <= 0 || port > 65535)
                    issues.Add($"SFTP Port must be between 1 and 65535 (current: {port})");
                
                if (string.IsNullOrEmpty(_settings.Sftp.RemoteDirectory))
                    issues.Add("SFTP RemoteDirectory should be specified (using '/' as default)");
                else
                {
                    // Validate remote directory path format
                    var remotePath = _settings.Sftp.RemoteDirectory;
                    if (remotePath.Contains('\\'))
                    {
                        issues.Add($"RemoteDirectory should use forward slashes, not backslashes: '{remotePath}'");
                    }
                    
                    // Check for common path issues
                    if (remotePath.Contains(".."))
                    {
                        issues.Add($"RemoteDirectory contains relative path notation (..) which may cause issues: '{remotePath}'");
                    }
                    
                    if (remotePath.EndsWith("/") && remotePath.Length > 1)
                    {
                        _logger.LogInformation("RemoteDirectory ends with '/' - this will be normalized: '{RemotePath}'", remotePath);
                    }
                }
            }
            
            return (issues.Count == 0, issues.ToArray());
        }

        /// <summary>
        /// Performs comprehensive diagnostics of SFTP connectivity and path accessibility
        /// </summary>
        /// <returns>Diagnostic result with detailed information</returns>
        public async Task<(bool Success, string[] Details)> RunDiagnosticsAsync()
        {
            var details = new List<string>();
            var overallSuccess = true;

            try
            {
                _logger.LogInformation("=== Starting SFTP Diagnostics ===");
                details.Add("Starting SFTP diagnostics...");

                // Step 1: Validate settings
                var settingsValidation = ValidateSettings();
                if (!settingsValidation.IsValid)
                {
                    details.Add("? Settings validation failed:");
                    details.AddRange(settingsValidation.Issues.Select(issue => $"   • {issue}"));
                    overallSuccess = false;
                    return (overallSuccess, details.ToArray());
                }
                details.Add("? Settings validation passed");

                // Step 2: Test basic connection
                var connectionInfo = GetConnectionInfo();
                if (connectionInfo == null)
                {
                    details.Add("? Failed to create connection info");
                    overallSuccess = false;
                    return (overallSuccess, details.ToArray());
                }

                using var testClient = new SftpClient(connectionInfo);
                
                try
                {
                    await Task.Run(() => testClient.Connect());
                    details.Add("? Basic SFTP connection successful");
                }
                catch (Exception ex)
                {
                    details.Add($"? Connection failed: {ex.Message}");
                    overallSuccess = false;
                    return (overallSuccess, details.ToArray());
                }

                // Step 3: Test remote directory access
                var remoteDir = _settings.Sftp?.RemoteDirectory ?? "/";
                details.Add($"Testing remote directory: '{remoteDir}'");

                try
                {
                    bool directoryExists = false;
                    bool canListContents = false;
                    
                    // First, try the basic existence check
                    try
                    {
                        directoryExists = testClient.Exists(remoteDir);
                        if (directoryExists)
                        {
                            details.Add($"? Remote directory exists: '{remoteDir}'");
                            
                            // Check if it's actually a directory
                            var attributes = testClient.GetAttributes(remoteDir);
                            if (!attributes.IsDirectory)
                            {
                                details.Add($"? Path exists but is not a directory: '{remoteDir}'");
                                overallSuccess = false;
                            }
                            else
                            {
                                details.Add("? Confirmed path is a directory");
                            }
                        }
                        else
                        {
                            details.Add($"? Remote directory does not exist: '{remoteDir}'");
                            overallSuccess = false;
                        }
                    }
                    catch (Renci.SshNet.Common.SftpPermissionDeniedException)
                    {
                        details.Add($"? Permission denied checking directory existence: '{remoteDir}'");
                        details.Add("?? Directory may exist but user lacks read permissions");
                        
                        // Try alternative method - attempt to list directory
                        try
                        {
                            var items = testClient.ListDirectory(remoteDir).Take(1).ToList();
                            directoryExists = true;
                            canListContents = true;
                            details.Add("? Directory appears to exist (can list contents)");
                        }
                        catch (Renci.SshNet.Common.SftpPermissionDeniedException)
                        {
                            details.Add("? Permission denied listing directory contents");
                            details.Add("?? User lacks both read and list permissions");
                            overallSuccess = false;
                        }
                        catch (Renci.SshNet.Common.SftpPathNotFoundException)
                        {
                            details.Add($"? Directory does not exist: '{remoteDir}'");
                            overallSuccess = false;
                        }
                    }
                    
                    // Provide helpful suggestions if there are issues
                    if (!directoryExists)
                    {
                        if (remoteDir != "/")
                        {
                            details.Add("?? Running path troubleshooting...");
                            
                            // Run comprehensive path troubleshooting
                            var pathTests = await TroubleshootDirectoryPathAsync(testClient, remoteDir);
                            
                            var workingDirs = pathTests.Where(p => p.Status == "DIRECTORY").ToArray();
                            var permissionDenied = pathTests.Where(p => p.Status == "PERMISSION_DENIED").ToArray();
                            
                            if (workingDirs.Length > 0)
                            {
                                details.Add("?? Found accessible directories:");
                                foreach (var dir in workingDirs.Take(3))
                                {
                                    details.Add($"   ? {dir.Path}");
                                }
                                details.Add("?? Consider using one of these paths in your configuration");
                            }
                            
                            if (permissionDenied.Length > 0)
                            {
                                details.Add("?? Paths with permission issues:");
                                foreach (var denied in permissionDenied.Take(3))
                                {
                                    details.Add($"   ? {denied.Path}");
                                }
                            }
                            
                            details.Add("?? General suggestions:");
                            details.Add("   • Check if the directory path is correct");
                            details.Add("   • Verify the directory exists on the server");
                            details.Add("   • Ensure the SFTP user has appropriate permissions");
                            details.Add("   • Try using an absolute path (starting with /)");
                            details.Add("   • Contact your system administrator for permission issues");
                        }
                    }
                }
                catch (Exception ex)
                {
                    details.Add($"? Error accessing remote directory: {ex.Message}");
                    overallSuccess = false;
                }

                // Step 4: Test directory listing (only if we haven't already tested it)
                if (overallSuccess)
                {
                    try
                    {
                        var files = testClient.ListDirectory(remoteDir).Take(10).ToList();
                        details.Add($"? Directory listing successful ({files.Count} items found)");
                        
                        if (files.Count > 0)
                        {
                            var regularFiles = files.Where(f => f.IsRegularFile).Count();
                            var directories = files.Where(f => f.IsDirectory && f.Name != "." && f.Name != "..").Count();
                            details.Add($"   • Files: {regularFiles}, Subdirectories: {directories}");
                        }
                    }
                    catch (Exception ex)
                    {
                        details.Add($"? Directory listing failed: {ex.Message}");
                        overallSuccess = false;
                    }
                }

                // Step 5: Test write permissions
                if (overallSuccess)
                {
                    if (TestWritePermissions(testClient, remoteDir))
                    {
                        details.Add("? Write permissions confirmed");
                    }
                    else
                    {
                        details.Add("? Write permission test failed");
                        details.Add("?? Uploads may fail due to insufficient permissions");
                    }
                }

                testClient.Disconnect();
                
                if (overallSuccess)
                {
                    details.Add("? All diagnostics passed - SFTP is ready for file uploads");
                }
                else
                {
                    details.Add("? Diagnostics completed with issues - please resolve before uploading");
                }

                _logger.LogInformation("=== SFTP Diagnostics Completed ===");
                
                return (overallSuccess, details.ToArray());
            }
            catch (Exception ex)
            {
                details.Add($"? Diagnostics failed with error: {ex.Message}");
                _logger.LogError(ex, "Error during SFTP diagnostics");
                return (false, details.ToArray());
            }
        }

        /// <summary>
        /// Troubleshoots the remote directory path to identify access issues
        /// </summary>
        /// <param name="client">Connected SFTP client</param>
        /// <param name="remoteDir">Remote directory to troubleshoot</param>
        /// <returns>List of path test results</returns>
        private async Task<PathTestResult[]> TroubleshootDirectoryPathAsync(SftpClient client, string remoteDir)
        {
            var tests = new List<PathTestResult>();
            
            // Test 1: Check if the directory exists
            try
            {
                bool exists = await Task.Run(() => client.Exists(remoteDir));
                tests.Add(new PathTestResult(remoteDir, exists ? "DIRECTORY" : "NOT_FOUND"));
            }
            catch (Renci.SshNet.Common.SftpPermissionDeniedException)
            {
                tests.Add(new PathTestResult(remoteDir, "PERMISSION_DENIED"));
            }
            catch (Exception ex)
            {
                tests.Add(new PathTestResult(remoteDir, $"ERROR: {ex.Message}"));
            }
            
            // Test 2: Try to list the directory contents
            try
            {
                var items = await Task.Run(() => client.ListDirectory(remoteDir).ToList());
                tests.Add(new PathTestResult(remoteDir, "DIRECTORY", items.Count));
            }
            catch (Renci.SshNet.Common.SftpPermissionDeniedException)
            {
                tests.Add(new PathTestResult(remoteDir, "PERMISSION_DENIED"));
            }
            catch (Renci.SshNet.Common.SftpPathNotFoundException)
            {
                tests.Add(new PathTestResult(remoteDir, "NOT_FOUND"));
            }
            catch (Exception ex)
            {
                tests.Add(new PathTestResult(remoteDir, $"ERROR: {ex.Message}"));
            }
            
            // Test 3: Check write permission by creating a test file
            try
            {
                var testFileName = $".temp_test_{Guid.NewGuid():N}";
                var testFilePath = CombinePath(remoteDir, testFileName);
                
                using var testStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("temp file"));
                client.UploadFile(testStream, testFilePath);
                
                // If upload succeeds, we have write permission
                tests.Add(new PathTestResult(remoteDir, "WRITABLE"));
                
                // Clean up the test file
                client.DeleteFile(testFilePath);
            }
            catch (Renci.SshNet.Common.SftpPermissionDeniedException)
            {
                tests.Add(new PathTestResult(remoteDir, "WRITE_PERMISSION_DENIED"));
            }
            catch (Exception ex)
            {
                tests.Add(new PathTestResult(remoteDir, $"ERROR: {ex.Message}"));
            }
            
            return tests.ToArray();
        }

        /// <summary>
        /// Combines path segments for remote SFTP paths (always uses forward slashes)
        /// </summary>
        /// <param name="path1">First path segment</param>
        /// <param name="path2">Second path segment</param>
        /// <returns>Combined path</returns>
        private static string CombinePath(string path1, string path2)
        {
            // Ensure we use forward slashes for SFTP paths
            var cleanPath1 = path1.Replace('\\', '/').TrimEnd('/');
            var cleanPath2 = path2.Replace('\\', '/').TrimStart('/');
            
            if (string.IsNullOrEmpty(cleanPath1))
                return cleanPath2;
            if (string.IsNullOrEmpty(cleanPath2))
                return cleanPath1;
                
            return $"{cleanPath1}/{cleanPath2}";
        }

        /// <summary>
        /// Disposes the SFTP client
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                Disconnect();
                _sftpClient?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during SFTP service disposal");
            }
            finally
            {
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Represents the result of a path test during diagnostics
    /// </summary>
    public class PathTestResult
    {
        public string Path { get; }
        public string Status { get; }
        public int ItemCount { get; }

        public PathTestResult(string path, string status, int itemCount = 0)
        {
            Path = path;
            Status = status;
            ItemCount = itemCount;
        }
    }
}