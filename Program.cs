using Systems_One_Sftp_Upload_Service;
using Serilog;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using System;
using System.IO;
using System.Text.Json;

try
{
    // Determine log directory based on build configuration
    string logDirectory;
#if DEBUG
    logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
#else
    logDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
        "SysOne_Upload_Service",
        "logs");
#endif

    // Ensure directory exists
    Directory.CreateDirectory(logDirectory);

    // Set up log file path
    string uploadLogPath = Path.Combine(logDirectory, "uploadlog.log");

    // Daily log rollover - delete log file if it's from a previous day
    if (File.Exists(uploadLogPath) && File.GetLastWriteTime(uploadLogPath).Date < DateTime.Now.Date)
        File.Delete(uploadLogPath);

    // Configure minimum log level based on build configuration
#if DEBUG
    var logLevel = LogEventLevel.Debug;
#else
    var logLevel = LogEventLevel.Information;
#endif

    // Configure Serilog
    Log.Logger = new LoggerConfiguration()
#if DEBUG
        .WriteTo.Console(
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
        )
#endif
        .WriteTo.File(
            uploadLogPath,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
            shared: true
        )
        .Enrich.FromLogContext()
        .MinimumLevel.Is(logLevel)
        .CreateLogger();

    Log.Information("Log files will be written to {LogsFolder}", Path.GetDirectoryName(uploadLogPath));

    // Determine settings path based on build configuration
    string settingsPath;
#if DEBUG
    settingsPath = "upload_settings.json";
#else
    var publicDocs = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
    var settingsFolder = Path.Combine(publicDocs, "SystemOne_App_Settings");
    settingsPath = Path.Combine(settingsFolder, "upload_settings.json");
#endif

    // Check if settings file exists
    if (!File.Exists(settingsPath))
    {
        Log.Error("ERROR: '{SettingsPath}' not found.", settingsPath);
#if DEBUG
        Console.Error.WriteLine($"Settings file '{settingsPath}' not found.");
#endif
        // Create a sample settings file for demonstration purposes
        CreateSampleSettingsFile(settingsPath);
        Log.Information("Created a sample settings file at '{SettingsPath}'", settingsPath);
    }

    // Load settings
    var settings = LoadSettings(settingsPath);
    Log.Information("Loaded settings from {Path}", settingsPath);
#if DEBUG
    Log.Debug("Settings content:\n{Settings}", JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Settings loaded from {settingsPath}");
    Console.WriteLine(JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
#endif

    // Create the host with Serilog
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddHostedService<Worker>();
    
    // Register Serilog
    builder.Services.AddLogging(loggingBuilder => 
        loggingBuilder.AddSerilog(dispose: true));

    // Register Settings
    builder.Services.AddSingleton(settings);

    var host = builder.Build();
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application start-up failed: {Message}", ex.Message);
}
finally
{
    // Ensure to flush and close the log
    Log.CloseAndFlush();
}

// Settings classes
public class UploadSettings
{
    public string? UploadDirectory { get; set; }
    public string? ArchiveDirectory { get; set; }
    public int PollingIntervalSeconds { get; set; } = 60; // Default to 60 seconds
    public SftpSettings? Sftp { get; set; }
    public DatabaseSettings? Database { get; set; }
}

public class SftpSettings
{
    public string? Host { get; set; }
    public int Port { get; set; } = 22; // Default SFTP port
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? RemoteDirectory { get; set; }
}

public class DatabaseSettings
{
    public string? Server { get; set; }
    public string? DatabaseName { get; set; }
    public string? TableName { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}

// Load settings from file
static UploadSettings LoadSettings(string path)
{
    try
    {
        string json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<UploadSettings>(json);
        
        if (settings == null)
        {
            throw new JsonException("Failed to deserialize settings. The JSON content may be invalid.");
        }
        
        // Validate required properties or apply defaults
        ValidateSettings(settings);
        
        return settings;
    }
    catch (Exception ex) when (ex is IOException || ex is JsonException)
    {
        Log.Error(ex, "Failed to load settings from {Path}. Error: {Error}", path, ex.Message);
        throw;
    }
}

// Validate settings and set defaults
static void ValidateSettings(UploadSettings settings)
{
    // Validate upload directory
    if (string.IsNullOrEmpty(settings.UploadDirectory))
    {
        Log.Warning("Upload directory is not specified in settings. Using default upload directory.");
        settings.UploadDirectory = Path.Combine(AppContext.BaseDirectory, "uploads");
    }
    
    // Validate archive directory
    if (string.IsNullOrEmpty(settings.ArchiveDirectory))
    {
        Log.Warning("Archive directory is not specified in settings. Using default archive directory.");
        settings.ArchiveDirectory = Path.Combine(AppContext.BaseDirectory, "archive");
    }
    
    // Add more validations as needed
}

// Create a sample settings file for demonstration
static void CreateSampleSettingsFile(string path)
{
    try
    {
        // Ensure directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
        
        var settings = new UploadSettings
        {
            UploadDirectory = Path.Combine(AppContext.BaseDirectory, "uploads"),
            ArchiveDirectory = Path.Combine(AppContext.BaseDirectory, "archive"),
            PollingIntervalSeconds = 60,
            Sftp = new SftpSettings
            {
                Host = "sftp.example.com",
                Port = 22,
                Username = "user",
                Password = "password",
                RemoteDirectory = "/uploads"
            },
            Database = new DatabaseSettings
            {
                Server = "localhost",
                DatabaseName = "UploadDb",
                TableName = "UploadLog",
                Username = "dbuser",
                Password = "dbpass"
            }
        };
        
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to create sample settings file at {Path}. Error: {Error}", path, ex.Message);
    }
}
