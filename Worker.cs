using Serilog;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

namespace Systems_One_Sftp_Upload_Service
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private Settings? _settings;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
            
            // Initialize Serilog in case it's not already configured
            if (Log.Logger == Serilog.Core.Logger.None)
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
                var logLevel = Serilog.Events.LogEventLevel.Debug;
            #else
                var logLevel = Serilog.Events.LogEventLevel.Information;
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
            }
            
            // Load settings
            LoadSettings();
        }

        private void LoadSettings()
        {
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

            try
            {
                string json = File.ReadAllText(settingsPath);
                _settings = JsonSerializer.Deserialize<Settings>(json);
                
                if (_settings == null)
                {
                    throw new JsonException("Failed to deserialize settings. The JSON content may be invalid.");
                }
                
                // Validate required properties or apply defaults
                ValidateSettings(_settings);
                
                Log.Information("Loaded settings from {Path}", settingsPath);
            #if DEBUG
                Log.Debug("Settings content:\n{Settings}", JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"Settings loaded from {settingsPath}");
                Console.WriteLine(JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
            #endif
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException)
            {
                Log.Error(ex, "Failed to load settings from {Path}. Error: {Error}", settingsPath, ex.Message);
                _settings = new Settings(); // Use default settings
                ValidateSettings(_settings);
            }
        }

        // Validate settings and set defaults
        private void ValidateSettings(Settings settings)
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
        private void CreateSampleSettingsFile(string path)
        {
            try
            {
                // Ensure directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
                
                var sftpSettings = new SftpSettingsImpl
                {
                    Host = "sftp.example.com",
                    Port = 22,
                    Username = "user",
                    Password = "password",
                    RemoteDirectory = "/uploads"
                };
                
                var dbSettings = new DatabaseSettingsImpl
                {
                    Server = "localhost",
                    DatabaseName = "UploadDb",
                    TableName = "UploadLog",
                    Username = "dbuser",
                    Password = "dbpass"
                };
                
                var settings = new Settings
                {
                    UploadDirectory = Path.Combine(AppContext.BaseDirectory, "uploads"),
                    ArchiveDirectory = Path.Combine(AppContext.BaseDirectory, "archive"),
                    PollingIntervalSeconds = 60,
                    Sftp = sftpSettings,
                    Database = dbSettings
                };
                
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to create sample settings file at {Path}. Error: {Error}", path, ex.Message);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                    
                    // Log information about the settings if available
                    if (_settings != null)
                    {
                        Log.Information("Upload Directory: {UploadDir}", _settings.UploadDirectory);
                        Log.Information("Archive Directory: {ArchiveDir}", _settings.ArchiveDirectory);
                        Log.Information("Polling Interval: {Interval} seconds", _settings.PollingIntervalSeconds);
                        
                        if (_settings.Sftp != null)
                        {
                            Log.Information("SFTP Host: {Host}:{Port}", _settings.Sftp.Host, _settings.Sftp.Port);
                            Log.Information("SFTP Remote Directory: {RemoteDir}", _settings.Sftp.RemoteDirectory);
                        }
                    }
                }
                
                // Use the polling interval from settings if available
                int delay = _settings?.PollingIntervalSeconds ?? 60;
                await Task.Delay(delay * 1000, stoppingToken);
            }
        }
    }

    // Settings classes
    public class Settings
    {
        public string? UploadDirectory { get; set; }
        public string? ArchiveDirectory { get; set; }
        public int PollingIntervalSeconds { get; set; } = 60; // Default to 60 seconds
        public SftpSettingsImpl? Sftp { get; set; }
        public DatabaseSettingsImpl? Database { get; set; }
    }

    public class SftpSettingsImpl
    {
        public string? Host { get; set; }
        public int Port { get; set; } = 22; // Default SFTP port
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? RemoteDirectory { get; set; }
    }

    public class DatabaseSettingsImpl
    {
        public string? Server { get; set; }
        public string? DatabaseName { get; set; }
        public string? TableName { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
    }
}
