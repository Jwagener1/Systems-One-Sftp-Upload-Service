using Systems_One_Sftp_Upload_Service;
using Systems_One_Sftp_Upload_Service.Services;
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
    // For DEBUG, use project root directory instead of bin folder
    var projectRoot = Directory.GetCurrentDirectory();
    logDirectory = Path.Combine(projectRoot, "logs");
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

    // Configure Serilog - console output only in DEBUG mode
    var loggerConfig = new LoggerConfiguration()
        .WriteTo.File(
            uploadLogPath,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
            shared: true
        )
        .Enrich.FromLogContext()
        .MinimumLevel.Is(logLevel);

#if DEBUG
    // Add console output only in DEBUG mode
    loggerConfig.WriteTo.Console(
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
    );
#endif

    Log.Logger = loggerConfig.CreateLogger();

    Log.Information("Systems One SFTP Upload Service starting...");
    Log.Information("Build Configuration: {BuildConfig}", 
#if DEBUG
        "DEBUG"
#else
        "RELEASE"
#endif
    );
    Log.Information("Current Directory: {CurrentDir}", Directory.GetCurrentDirectory());
    Log.Information("Log files will be written to: {LogsFolder}", Path.GetDirectoryName(uploadLogPath));

    // Get settings path using centralized method
    string settingsPath = SettingsLoader.GetSettingsPath();
    Log.Information("Looking for settings file: {SettingsPath}", settingsPath);

    // Check if settings file exists
    if (!File.Exists(settingsPath))
    {
        Log.Error("Settings file '{SettingsPath}' not found", settingsPath);
        
        // Show helpful information about where to put the settings file
        Log.Information("Please ensure the settings file exists at the correct location:");
#if DEBUG
        Log.Information("DEBUG mode: Current directory - {CurrentDir}\\upload_settings.json", Directory.GetCurrentDirectory());
#else
        Log.Information("RELEASE mode: Public Documents - {SettingsPath}", settingsPath);
#endif
        
        // Try to create a sample settings file for demonstration purposes
        try
        {
            SettingsLoader.CreateSampleSettingsFile(settingsPath);
            Log.Information("Created a sample settings file at '{SettingsPath}'", settingsPath);
            Log.Information("Please review and update the settings before running the service again");
        }
        catch (Exception createEx)
        {
            Log.Error(createEx, "Failed to create sample settings file");
            Log.Information("You may need to manually create the settings file or run as administrator");
        }
        
#if DEBUG
        // Only wait for user input in DEBUG mode
        Console.WriteLine($"\nSettings file not found: {settingsPath}");
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
#else
        // In RELEASE mode, exit immediately without console interaction
        Log.Error("Service cannot start without settings file. Exiting.");
#endif
        return;
    }

    // Load settings using the SettingsLoader
    var settings = SettingsLoader.LoadJson(settingsPath);
    Log.Information("Successfully loaded settings from {Path}", settingsPath);
#if DEBUG
    Log.Debug("Settings loaded successfully");
#endif

    // Create the host with Serilog
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddHostedService<Worker>();
    
    // Register Serilog
    builder.Services.AddLogging(loggingBuilder => 
        loggingBuilder.AddSerilog(dispose: true));

    // Register Settings as Singleton
    builder.Services.AddSingleton(settings);
    
    // Register MessageFormatter and StringFormatter as Singletons (not Scoped)
    // This fixes the DI scope issue since HostedService is Singleton
    builder.Services.AddSingleton<MessageFormatter>(provider =>
    {
        var logger = provider.GetRequiredService<ILogger<MessageFormatter>>();
        return new MessageFormatter(settings.MessageFormat!, logger);
    });
    
    builder.Services.AddSingleton<StringFormatter>(provider =>
    {
        var logger = provider.GetRequiredService<ILogger<StringFormatter>>();
        return new StringFormatter(settings.MessageFormat!, logger);
    });
    
    // Register FileCreationService for file management
    builder.Services.AddSingleton<FileCreationService>();
    
    // Register SftpUploadService for SFTP functionality
    builder.Services.AddSingleton<SftpUploadService>();
    
    // Register Data Retrieval Service
    // Use SQL Server service in production, sample service for demo
    if (settings.Database != null && !string.IsNullOrEmpty(settings.Database.Server))
    {
        builder.Services.AddSingleton<IDataRetrievalService, SqlServerDataRetrievalService>();
        Log.Information("Registered SQL Server data retrieval service");
    }
    else
    {
        builder.Services.AddSingleton<IDataRetrievalService, SampleDataRetrievalService>();
        Log.Warning("Database settings not configured - using sample data service");
    }

    Log.Information("All services registered successfully");
    Log.Information("Starting worker service...");
    
    var host = builder.Build();
    
    // Log startup completion
    Log.Information("Host built successfully, starting main service loop...");
#if DEBUG
    Log.Information("Service is running. Press Ctrl+C to stop.");
#else
    Log.Information("Service is running in background mode.");
#endif
    
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application start-up failed: {Message}", ex.Message);
    
#if DEBUG
    // Show error in console and wait for input only in DEBUG mode
    Console.WriteLine($"Fatal error: {ex.Message}");
    Console.WriteLine($"Error details: {ex}");
    Console.WriteLine("Check the log file for more details.");
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey();
#else
    // In RELEASE mode, just log the error and exit
    Log.Error("Service failed to start. Check log file for details.");
#endif
}
finally
{
    Log.Information("Systems One SFTP Upload Service shutting down...");
    // Ensure to flush and close the log
    Log.CloseAndFlush();
}