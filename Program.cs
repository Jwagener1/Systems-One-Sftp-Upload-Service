using Systems_One_Sftp_Upload_Service;
using Serilog;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using System;
using System.IO;

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

    // Get settings path using centralized method
    string settingsPath = SettingsLoader.GetSettingsPath();

    // Check if settings file exists
    if (!File.Exists(settingsPath))
    {
        Log.Error("Settings file '{SettingsPath}' not found", settingsPath);
#if DEBUG
        Console.Error.WriteLine($"Settings file '{settingsPath}' not found");
#endif
        // Create a sample settings file for demonstration purposes
        SettingsLoader.CreateSampleSettingsFile(settingsPath);
        Log.Information("Created a sample settings file at '{SettingsPath}'", settingsPath);
    }

    // Load settings using the SettingsLoader
    var settings = SettingsLoader.LoadJson(settingsPath);
    Log.Information("Loaded settings from {Path}", settingsPath);
#if DEBUG
    Log.Debug("Settings loaded successfully");
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
