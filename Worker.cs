using Serilog;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Systems_One_Sftp_Upload_Service.Models;
using Systems_One_Sftp_Upload_Service.Services;

namespace Systems_One_Sftp_Upload_Service
{
    /// <summary>
    /// Worker service for handling SFTP uploads
    /// </summary>
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly AppSettings _settings;
        private readonly MessageFormatter _messageFormatter;
        private readonly FileCreationService _fileCreationService;
        private readonly SftpUploadService _sftpUploadService;
        private readonly IDataRetrievalService _dataRetrievalService;
        private bool _hasDemonstratedFormatter = false;
        private bool _hasCreatedDebugFiles = false;
        private bool _hasTestedSftpConnection = false;
        private bool _hasTestedDatabaseConnection = false;

        /// <summary>
        /// Initializes a new instance of the Worker class
        /// </summary>
        /// <param name="logger">Logger instance</param>
        /// <param name="settings">Application settings</param>
        /// <param name="messageFormatter">Message formatter instance</param>
        /// <param name="fileCreationService">File creation service instance</param>
        /// <param name="sftpUploadService">SFTP upload service instance</param>
        /// <param name="dataRetrievalService">Data retrieval service instance</param>
        public Worker(ILogger<Worker> logger, AppSettings settings, MessageFormatter messageFormatter, 
            FileCreationService fileCreationService, SftpUploadService sftpUploadService, 
            IDataRetrievalService dataRetrievalService)
        {
            _logger = logger;
            _settings = settings;
            _messageFormatter = messageFormatter;
            _fileCreationService = fileCreationService;
            _sftpUploadService = sftpUploadService;
            _dataRetrievalService = dataRetrievalService;
        }

        /// <summary>
        /// Main worker process that runs continuously
        /// </summary>
        /// <param name="stoppingToken">Token for cancellation notification</param>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("=== Systems One SFTP Upload Service Starting ===");
            
            // Initial SFTP settings validation
            await ValidateSftpSettingsAsync();
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                        
                        // Log information about the settings
                        if (_settings.General != null)
                        {
                            _logger.LogInformation("Upload Interval: {Interval} ms", _settings.General.GetUploadIntervalMs());
                        }
                        
                        if (_settings.Sftp != null)
                        {
                            _logger.LogInformation("SFTP Host: {Host}:{Port}", _settings.Sftp.Host, _settings.Sftp.GetPort());
                            _logger.LogInformation("SFTP Remote Directory: {RemoteDir}", _settings.Sftp.RemoteDirectory);
                        }
                    }
                    
                    // One-time demonstrations (DEBUG mode only)
#if DEBUG
                    if (!_hasDemonstratedFormatter)
                    {
                        await DemonstrateMessageFormatter();
                        _hasDemonstratedFormatter = true;
                    }
                    
                    if (!_hasCreatedDebugFiles)
                    {
                        await CreateDebugFilesForVerification();
                        _hasCreatedDebugFiles = true;
                        
                        await DemonstrateDebugVsProductionFiles();
                        await DemonstrateFileArchiving();
                    }
                    
                    if (!_hasTestedSftpConnection)
                    {
                        await DemonstrateSftpConnection();
                        _hasTestedSftpConnection = true;
                    }
                    
                    if (!_hasTestedDatabaseConnection)
                    {
                        await DemonstrateDatabaseConnection();
                        _hasTestedDatabaseConnection = true;
                    }
#endif
                    
                    // Main production workflow: Upload files
                    await ProcessUploadWorkflowAsync();
                    
                    // Periodic cleanup of old archived files
                    await PerformPeriodicCleanupAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in worker execution cycle");
                }
                
                // Use the polling interval from settings
                int delay = _settings.General?.GetUploadIntervalMs() ?? 5000;
                await Task.Delay(delay, stoppingToken);
            }
            
            _logger.LogInformation("=== Systems One SFTP Upload Service Stopping ===");
        }

        /// <summary>
        /// Validates SFTP settings on startup
        /// </summary>
        private async Task ValidateSftpSettingsAsync()
        {
            try
            {
                _logger.LogInformation("=== Validating System Settings ===");
                
                // Validate SFTP settings
                var validation = _sftpUploadService.ValidateSettings();
                
                if (validation.IsValid)
                {
                    _logger.LogInformation("? SFTP settings validation passed");
                }
                else
                {
                    _logger.LogWarning("? SFTP settings validation issues:");
                    foreach (var issue in validation.Issues)
                    {
                        _logger.LogWarning("  - {Issue}", issue);
                    }
                }
                
                // Validate database connection
                _logger.LogInformation("Testing database connection...");
                var dbConnectionTest = await _dataRetrievalService.TestConnectionAsync();
                
                if (dbConnectionTest)
                {
                    _logger.LogInformation("? Database connection test successful");
                }
                else
                {
                    _logger.LogError("? Database connection test failed");
                    _logger.LogError("Data retrieval will not work until database issues are resolved");
                }

                // In production mode, also run basic connectivity tests
#if !DEBUG
                if (validation.IsValid)
                {
                    _logger.LogInformation("Running basic SFTP connectivity test...");
                    var connectionTest = await _sftpUploadService.TestConnectionAsync();
                    
                    if (connectionTest)
                    {
                        _logger.LogInformation("? SFTP connectivity test successful");
                    }
                    else
                    {
                        _logger.LogError("? SFTP connectivity test failed");
                        _logger.LogError("File uploads will not work until connectivity issues are resolved");
                        
                        // Provide basic troubleshooting info
                        _logger.LogError("Troubleshooting steps:");
                        _logger.LogError("1. Verify SFTP server is running and accessible");
                        _logger.LogError("2. Check network connectivity to {Host}:{Port}", 
                            _settings.Sftp?.Host, _settings.Sftp?.GetPort());
                        _logger.LogError("3. Verify username and password are correct");
                        _logger.LogError("4. Check that remote directory '{RemoteDir}' exists and is accessible", 
                            _settings.Sftp?.RemoteDirectory);
                    }
                }
#endif
                
                if (!validation.IsValid)
                {
                    _logger.LogWarning("SFTP upload functionality will not work until these issues are resolved");
                    
                    // Provide helpful guidance
                    _logger.LogInformation("To resolve SFTP settings issues:");
                    _logger.LogInformation("1. Check your upload_settings.json file");
                    _logger.LogInformation("2. Ensure all required SFTP fields are populated");
                    _logger.LogInformation("3. Verify the RemoteDirectory path format uses forward slashes");
                    _logger.LogInformation("4. Test connection manually if possible");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating system settings");
            }
        }

        /// <summary>
        /// Main production workflow for processing uploads
        /// </summary>
        private async Task ProcessUploadWorkflowAsync()
        {
            try
            {
                // Method 1: Upload existing files from upload directory
                await ProcessExistingFilesAsync();
                
                // Method 2: Create and upload files from data source (e.g., database)
                await ProcessDataFromSourceAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in upload workflow" +
                    "=== Error Detail ===" +
                    ex.ToString());
            }
        }

        /// <summary>
        /// Processes existing files in the upload directory
        /// </summary>
        private async Task ProcessExistingFilesAsync()
        {
            try
            {
                // Get files ready for upload
                var filesToUpload = _fileCreationService.GetFilesReadyForUpload();
                
                if (filesToUpload.Length == 0)
                {
                    _logger.LogDebug("No existing files ready for upload");
                    return;
                }
                
                _logger.LogInformation("=== Processing Existing Files ===");
                _logger.LogInformation("Found {FileCount} existing files ready for upload", filesToUpload.Length);
                
                // Upload files with retry logic
                var uploadResults = await _sftpUploadService.UploadFilesWithRetryAsync(filesToUpload, maxRetries: 3, retryDelayMs: 2000);
                
                // Process results and archive successful uploads
                await ProcessUploadResultsAsync(filesToUpload, uploadResults);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing existing files");
            }
        }

        /// <summary>
        /// Processes data from database and creates upload files
        /// </summary>
        private async Task ProcessDataFromSourceAsync()
        {
            try
            {
#if DEBUG
                _logger.LogInformation("=== Processing Data from Database ===");
#endif
                
                // Retrieve data from database
                var dataRecords = await _dataRetrievalService.GetDataForUploadAsync();
                var recordList = dataRecords.ToList();
                
                if (recordList.Count == 0)
                {
                    _logger.LogDebug("No new data available from database");
                    return;
                }
                
#if DEBUG
                _logger.LogInformation("Retrieved {RecordCount} records from database", recordList.Count);
#else
                // In RELEASE mode, log each record individually with barcode for better tracking
                foreach (var record in recordList)
                {
                    string barcode = record.Data.ContainsKey("Barcode") ? 
                        record.Data["Barcode"]?.ToString() ?? "Unknown" : "Unknown";
                    _logger.LogInformation("Found unsent entry, barcode: {Barcode}", barcode);
                }
#endif
                
                // Create production files from database data
                var dataForFiles = recordList.Select(r => r.Data).ToList();
                var createdFiles = await _fileCreationService.CreateProductionFilesFromDataAsync(dataForFiles);
                
                if (createdFiles.Length > 0)
                {
#if DEBUG
                    _logger.LogInformation("Created {FileCount} production files, starting upload", createdFiles.Length);
#endif
                    
                    // Upload the newly created files
                    var uploadResults = await _sftpUploadService.UploadFilesWithRetryAsync(createdFiles, maxRetries: 3, retryDelayMs: 2000);
                    
                    // Process results and archive successful uploads
                    await ProcessUploadResultsAsync(createdFiles, uploadResults);
                    
                    // Mark successfully uploaded records as processed
                    var successfullyUploadedFiles = new List<string>();
                    for (int i = 0; i < createdFiles.Length; i++)
                    {
                        if (uploadResults[i])
                        {
#if !DEBUG
                            _logger.LogInformation("File uploaded successfully: {FileName}", 
                                Path.GetFileName(createdFiles[i]));
#endif
                            successfullyUploadedFiles.Add(createdFiles[i]);
                        }
                    }
                    
                    if (successfullyUploadedFiles.Count > 0)
                    {
                        // Get the record IDs for successfully uploaded files
                        var recordIdsToMark = recordList.Take(successfullyUploadedFiles.Count).Select(r => r.RecordId);
                        
                        try
                        {
                            await _dataRetrievalService.MarkRecordsAsProcessedAsync(recordIdsToMark);
#if DEBUG
                            _logger.LogInformation("Marked {RecordCount} database records as processed", recordIdsToMark.Count());
#endif
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to mark records as processed in database");
                            _logger.LogWarning("Records were uploaded but not marked as processed - may be uploaded again");
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("No files were created from database data");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing data from database");
            }
        }

        /// <summary>
        /// Checks if there's new data available (sample implementation)
        /// In production, this would check your data source
        /// </summary>
        private async Task<bool> CheckForNewDataAsync()
        {
            // Simulate checking for new data
            await Task.Delay(10);
            
            // For demo purposes, only create new data occasionally
            // In production, this would check your actual data source
            var random = new Random();
            var hasNewData = random.Next(1, 10) > 7; // 30% chance of new data
            
            if (hasNewData)
            {
                _logger.LogInformation("New data detected from external source");
            }
            
            return hasNewData;
        }

        /// <summary>
        /// Creates sample data for production demonstration
        /// In production, this would be replaced by actual data retrieval
        /// </summary>
        private List<Dictionary<string, object>> CreateSampleDataForProduction()
        {
            var random = new Random();
            var sampleData = new List<Dictionary<string, object>>();
            
            // Create 1-2 sample records
            var recordCount = random.Next(1, 3);
            
            for (int i = 1; i <= recordCount; i++)
            {
                var record = new Dictionary<string, object>
                {
                    {"Weight_Ratio", Math.Round((decimal)(random.NextDouble() * 0.1 + 0.05), 3)},
                    {"LiquidVolume", Math.Round((decimal)(random.NextDouble() * 50.0 + 100.0), 2)},
                    {"Barcode", $"PRD{DateTime.Now:yyyyMMdd}{random.Next(100, 999)}"},
                    {"Length", random.Next(200, 350)},
                    {"Width", Math.Round((decimal)(random.NextDouble() * 2.0 + 2.0), 2)},
                    {"Height", random.Next(120, 200)},
                    {"RecordId", $"REC_{DateTime.Now:yyyyMMddHHmmss}_{i}"}
                };
                
                sampleData.Add(record);
            }
            
            return sampleData;
        }

        /// <summary>
        /// Processes upload results and archives successful uploads
        /// </summary>
        private async Task ProcessUploadResultsAsync(string[] filePaths, bool[] uploadResults)
        {
            var successfulUploads = new List<string>();
            var failedUploads = new List<string>();
            
            for (int i = 0; i < filePaths.Length; i++)
            {
                if (uploadResults[i])
                {
                    successfulUploads.Add(filePaths[i]);
                }
                else
                {
                    failedUploads.Add(filePaths[i]);
                }
            }
            
            _logger.LogInformation("Upload Results: {SuccessCount} successful, {FailedCount} failed", 
                successfulUploads.Count, failedUploads.Count);
            
            // Archive successful uploads if enabled
            if (successfulUploads.Count > 0 && (_settings.General?.AutoArchiveAfterUpload ?? true))
            {
                try
                {
                    _logger.LogInformation("Archiving {Count} successfully uploaded files", successfulUploads.Count);
                    var archivedPaths = await _fileCreationService.ArchiveFilesAsync(successfulUploads);
                    _logger.LogInformation("Successfully archived {ArchivedCount} files", archivedPaths.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error archiving uploaded files");
                }
            }
            
            // Log failed uploads for investigation
            if (failedUploads.Count > 0)
            {
                _logger.LogWarning("The following files failed to upload and remain in upload directory:");
                foreach (var failedFile in failedUploads)
                {
                    _logger.LogWarning("  - {FileName}", Path.GetFileName(failedFile));
                }
            }
        }

        /// <summary>
        /// Performs periodic cleanup of old archived files
        /// </summary>
        private async Task PerformPeriodicCleanupAsync()
        {
            try
            {
                // Only run cleanup occasionally (every hour for example)
                var now = DateTime.Now;
                if (now.Minute == 0) // Run at the top of each hour
                {
                    _logger.LogInformation("Performing periodic cleanup of old archived files");
                    
                    var retentionDays = _settings.General?.GetArchiveRetentionDays() ?? 30;
                    var deletedCount = _fileCreationService.CleanupOldArchivedFiles(retentionDays);
                    
                    if (deletedCount > 0)
                    {
                        _logger.LogInformation("Cleaned up {DeletedCount} old archived files", deletedCount);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during periodic cleanup");
            }
        }

#if DEBUG
        /// <summary>
        /// Demonstrates SFTP connection functionality (DEBUG only)
        /// </summary>
        private async Task DemonstrateSftpConnection()
        {
            try
            {
                _logger.LogInformation("=== SFTP Connection Demonstration ===");
                
                // Run comprehensive diagnostics
                var diagnostics = await _sftpUploadService.RunDiagnosticsAsync();
                
                _logger.LogInformation("SFTP Diagnostics Results:");
                foreach (var detail in diagnostics.Details)
                {
                    _logger.LogInformation("  {Detail}", detail);
                }
                
                if (diagnostics.Success)
                {
                    _logger.LogInformation("? SFTP system is ready for production use");
                    
                    // List remote files as additional verification
                    var remoteFiles = await _sftpUploadService.ListRemoteFilesAsync();
                    _logger.LogInformation("Remote directory contains {FileCount} files", remoteFiles.Length);
                    
                    if (remoteFiles.Length > 0)
                    {
                        _logger.LogInformation("Recent remote files:");
                        foreach (var file in remoteFiles.Take(5))
                        {
                            _logger.LogInformation("  - {FileName} ({Size} bytes, {ModTime})", 
                                file.Name, file.Length, file.LastWriteTime);
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("? SFTP system has issues - uploads may fail");
                    _logger.LogWarning("Please resolve the reported issues before production use");
                }
                
                _logger.LogInformation("=== End SFTP Connection Demonstration ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during SFTP connection demonstration");
            }
        }

        /// <summary>
        /// Creates debug files for verification (DEBUG only)
        /// </summary>
        private async Task CreateDebugFilesForVerification()
        {
            try
            {
                _logger.LogInformation("=== Creating Debug Files for Verification ===");
                
                // Get upload directory info
                var uploadDir = _fileCreationService.GetUploadDirectory();
                _logger.LogInformation("Upload directory: {UploadDirectory}", uploadDir);
                
                // Clean up old files first
                var deletedCount = _fileCreationService.CleanupOldFiles(5);
                if (deletedCount > 0)
                {
                    _logger.LogInformation("Cleaned up {DeletedCount} old files", deletedCount);
                }
                
                // Create sample files with current settings
                var sampleFiles = await _fileCreationService.CreateMultipleSampleFilesAsync(3);
                
                _logger.LogInformation("Created {FileCount} sample files:", sampleFiles.Length);
                foreach (var filePath in sampleFiles)
                {
                    var fileName = Path.GetFileName(filePath);
                    _logger.LogInformation("  - {FileName}", fileName);
                }
                
                // Create a custom file with specific test data
                var customData = new Dictionary<string, object>
                {
                    {"Weight_Ratio", 0.08m},
                    {"LiquidVolume", 131.31m},
                    {"Barcode", "TEST123456"},
                    {"Length", 290},
                    {"Width", 2.80m},
                    {"Height", 164}
                };
                
                var customFile = await _fileCreationService.CreateCustomUploadFileAsync(customData);
                _logger.LogInformation("Created custom test file: {FileName}", Path.GetFileName(customFile));
                
                // Show existing files
                var existingFiles = _fileCreationService.GetUploadFiles();
                _logger.LogInformation("Total files in upload directory: {FileCount}", existingFiles.Length);
                
                _logger.LogInformation("=== Debug Files Created Successfully ===");
                _logger.LogInformation("You can now verify the files in: {UploadDirectory}", uploadDir);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating debug files for verification");
            }
        }

        /// <summary>
        /// Demonstrates the message formatter functionality (DEBUG only)
        /// </summary>
        private Task DemonstrateMessageFormatter()
        {
            try
            {
                _logger.LogInformation("=== Message Formatter Demonstration ===");
                
                // Get formatting statistics
                var stats = _messageFormatter.GetFormattingStats();
                _logger.LogInformation("Formatting Statistics: {Stats}", stats.ToString());
                
                // Create and validate sample data
                var sampleData = _messageFormatter.CreateSampleData();
                _logger.LogInformation("Created sample data with {FieldCount} fields", sampleData.Count);
                
                // Log the sample data values for debugging
                foreach (var kvp in sampleData)
                {
                    _logger.LogDebug("Sample Data - {FieldType}: {Value} ({Type})", 
                        kvp.Key, kvp.Value, kvp.Value?.GetType().Name ?? "null");
                }
                
                // Validate the sample data
                var validation = _messageFormatter.ValidateData(sampleData);
                if (validation.IsValid)
                {
                    _logger.LogInformation("Sample data validation: PASSED");
                }
                else
                {
                    _logger.LogWarning("Sample data validation: FAILED - {ValidationResult}", validation.ToString());
                }
                
                // Format a single message
                var formattedMessage = _messageFormatter.FormatMessage(sampleData);
                _logger.LogInformation("Formatted message: '{FormattedMessage}'", formattedMessage);
                _logger.LogInformation("Message length: {Length} characters", formattedMessage.Length);
                
                // Show padding demonstration
                _logger.LogInformation("Padding demonstration:");
                _logger.LogInformation("- Numeric fields should be RIGHT-ALIGNED (padded on left)");
                _logger.LogInformation("- Text fields should be LEFT-ALIGNED (padded on right)");
                
                // Demonstrate decimal separator functionality
                var decimalSeparator = _settings.MessageFormat?.DecimalSeparator ?? ".";
                _logger.LogInformation("Current decimal separator: '{DecimalSeparator}'", decimalSeparator);
                
                // Show decimal separator demo
                var decimalDemo = _messageFormatter.CreateDecimalSeparatorDemo();
                _logger.LogInformation("Decimal separator demo result: '{DecimalDemo}'", decimalDemo);
                
                // Test decimal separator functionality with different values
                _messageFormatter.TestDecimalSeparatorFunctionality();
                
                // Test with comma separator to show difference
                if (decimalSeparator != ",")
                {
                    _logger.LogInformation("Testing with comma separator for comparison:");
                    _messageFormatter.TestDecimalSeparatorFunctionality(",");
                }
                
                // Generate a filename using FileSettings
                if (_settings.FileSettings != null)
                {
                    var fileName = _settings.FileSettings.GenerateFileName();
                    _logger.LogInformation("Generated filename: {FileName}", fileName);
                    
                    // Show DateTimeFormat information
                    var dateTimeFormat = _settings.FileSettings.GetDateTimeFormat();
                    
                    // Show several examples with different timestamps
                    var exampleDates = new[]
                    {
                        new DateTime(2025, 7, 22, 7, 39, 3),   // Your requested format
                        new DateTime(2025, 12, 31, 23, 59, 59), // End of year
                        DateTime.Now  // Current time
                    };
                    
                    _logger.LogInformation("File naming examples:");
                    foreach (var date in exampleDates)
                    {
                        var exampleFileName = _settings.FileSettings.GenerateFileName(date);
                        _logger.LogInformation("  {Date:yyyy-MM-dd HH:mm:ss} ? {FileName}", date, exampleFileName);
                    }
                }
                
                _logger.LogInformation("=== End Message Formatter Demonstration ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during message formatter demonstration");
            }
            
            return Task.CompletedTask;
        }

        /// <summary>
        /// Demonstrates the differences between debug and production file creation (DEBUG only)
        /// </summary>
        private async Task DemonstrateDebugVsProductionFiles()
        {
            try
            {
                _logger.LogInformation("=== Debug vs Production File Demonstration ===");
                
                var (debugFile, productionFile) = await _fileCreationService.CreateComparisonFilesAsync();
                
                // Read and show the content differences
                var debugContent = await File.ReadAllTextAsync(debugFile);
                var productionContent = await File.ReadAllTextAsync(productionFile);
                
                _logger.LogInformation("Debug file content ({Length} chars):", debugContent.Length);
                _logger.LogInformation("{DebugContent}", debugContent);
                
                _logger.LogInformation("Production file content ({Length} chars):", productionContent.Length);
                _logger.LogInformation("'{ProductionContent}'", productionContent);
                
                _logger.LogInformation("=== File Format Summary ===");
                _logger.LogInformation("DEBUG Mode: Files include headers for verification and debugging");
                _logger.LogInformation("RELEASE Mode: Files contain ONLY the formatted data for SFTP upload");
                _logger.LogInformation("Production file is {Difference} characters shorter", debugContent.Length - productionContent.Length);
                
                _logger.LogInformation("=== End Demonstration ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during debug vs production file demonstration");
            }
        }

        /// <summary>
        /// Demonstrates file archiving functionality (DEBUG only)
        /// </summary>
        private async Task DemonstrateFileArchiving()
        {
            try
            {
                _logger.LogInformation("=== File Archiving Demonstration ===");
                
                // Show archive configuration
                var retentionDays = _settings.General?.GetArchiveRetentionDays() ?? 30;
                var autoArchive = _settings.General?.AutoArchiveAfterUpload ?? true;
                
                _logger.LogInformation("Archive Configuration:");
                _logger.LogInformation("  Auto-archive after upload: {AutoArchive}", autoArchive);
                _logger.LogInformation("  Retention period: {RetentionDays} days", retentionDays);
                
                // Show directory structure
                var uploadDir = _fileCreationService.GetUploadDirectory();
                var archiveDir = _fileCreationService.GetArchiveDirectory();
                
                _logger.LogInformation("Directory Structure:");
                _logger.LogInformation("  Upload directory: {UploadDir}", uploadDir);
                _logger.LogInformation("  Archive directory: {ArchiveDir}", archiveDir);
                
                // Get files ready for upload (simulate SFTP upload process)
                var filesToUpload = _fileCreationService.GetFilesReadyForUpload();
                _logger.LogInformation("Files ready for upload: {FileCount}", filesToUpload.Length);
                
                if (filesToUpload.Length > 0)
                {
                    // Take the first file and demonstrate archiving
                    var fileToDemo = filesToUpload.First();
                    var fileName = Path.GetFileName(fileToDemo);
                    
                    _logger.LogInformation("Simulating SFTP upload and archiving for: {FileName}", fileName);
                    
                    // Simulate successful upload (in real implementation, this would be actual SFTP upload)
                    await Task.Delay(100); // Simulate upload time
                    
                    // Archive the file after "successful" upload
                    var archivedPath = await _fileCreationService.ArchiveFileAsync(fileToDemo);
                    
                    _logger.LogInformation("File archived successfully to: {ArchivedPath}", archivedPath);
                    
                    // Show archived files
                    var archivedFiles = _fileCreationService.GetArchivedFiles(DateTime.Today);
                    _logger.LogInformation("Archived files for today: {ArchivedCount}", archivedFiles.Length);
                    
                    foreach (var archivedFile in archivedFiles.Take(3)) // Show first 3
                    {
                        _logger.LogInformation("  - {FileName} ({Size} bytes)", archivedFile.Name, archivedFile.Length);
                    }
                }
                
                // Demonstrate archive cleanup (but don't actually delete anything in demo)
                _logger.LogInformation("Archive cleanup would delete files older than {RetentionDays} days", retentionDays);
                
                _logger.LogInformation("=== Production Workflow ===");
                _logger.LogInformation("1. Create file in upload directory");
                _logger.LogInformation("2. Upload file via SFTP");
                _logger.LogInformation("3. Archive file to date-organized folder");
                _logger.LogInformation("4. Periodic cleanup of old archived files");
                
                _logger.LogInformation("=== End File Archiving Demonstration ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during file archiving demonstration");
            }
        }

        /// <summary>
        /// Demonstrates database connection functionality (DEBUG only)
        /// </summary>
        private async Task DemonstrateDatabaseConnection()
        {
            try
            {
                _logger.LogInformation("=== Database Connection Demonstration ===");
                
                // Test database connection
                var connectionSuccess = await _dataRetrievalService.TestConnectionAsync();
                
                if (connectionSuccess)
                {
                    _logger.LogInformation("? Database connection test successful");
                    
                    // Get database statistics
                    var stats = await _dataRetrievalService.GetDatabaseStatisticsAsync();
                    _logger.LogInformation("Database Statistics:");
                    _logger.LogInformation("  Total Records: {TotalRecords}", stats.TotalRecords);
                    _logger.LogInformation("  Unsent Records: {UnsentRecords}", stats.UnsentRecords);
                    _logger.LogInformation("  Sent Records: {SentRecords}", stats.SentRecords);
                    
                    if (stats.OldestUnsent.HasValue)
                    {
                        _logger.LogInformation("  Oldest Unsent Record: {OldestUnsent:yyyy-MM-dd HH:mm:ss}", stats.OldestUnsent.Value);
                    }
                    
                    if (stats.NewestRecord.HasValue)
                    {
                        _logger.LogInformation("  Newest Record: {NewestRecord:yyyy-MM-dd HH:mm:ss}", stats.NewestRecord.Value);
                    }
                    
                    // Get sample data to show what would be uploaded
                    var sampleData = await _dataRetrievalService.GetDataForUploadAsync();
                    var dataList = sampleData.ToList();
                    
                    _logger.LogInformation("Found {RecordCount} records ready for upload", dataList.Count);
                    
                    if (dataList.Count > 0)
                    {
                        _logger.LogInformation("First unsent record data:");
                        var firstRecord = dataList[0];
                        foreach (var kvp in firstRecord.Data.Where(x => !x.Key.StartsWith("Record") && !x.Key.StartsWith("Item")))
                        {
                            _logger.LogInformation("  {Key}: {Value}", kvp.Key, kvp.Value);
                        }
                        _logger.LogInformation("  Record ID: {RecordId}", firstRecord.RecordId);
                        _logger.LogInformation("  ItemDateTime: {ItemDateTime}", firstRecord.Data.GetValueOrDefault("ItemDateTime"));
                        
                        if (dataList.Count > 1)
                        {
                            _logger.LogInformation("... and {RemainingCount} more records", dataList.Count - 1);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("No unprocessed records found in database");
                        if (stats.TotalRecords > 0)
                        {
                            _logger.LogInformation("All {TotalRecords} records have already been sent", stats.TotalRecords);
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("? Database connection test failed - data retrieval will not work");
                    _logger.LogWarning("Please check database settings and connectivity");
                }
                
                _logger.LogInformation("=== End Database Connection Demonstration ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during database connection demonstration");
            }
        }
#endif

        /// <summary>
        /// Dispose of resources when the service stops
        /// </summary>
        public override void Dispose()
        {
            try
            {
                _sftpUploadService?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing SFTP service");
            }
            finally
            {
                base.Dispose();
            }
        }
    }
}
