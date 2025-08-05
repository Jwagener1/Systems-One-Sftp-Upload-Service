using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using Serilog;
using Systems_One_Sftp_Upload_Service.Models;

namespace Systems_One_Sftp_Upload_Service
{
    /// <summary>
    /// Utility class for loading and managing settings
    /// </summary>
    public static class SettingsLoader
    {
        /// <summary>
        /// Loads settings from a JSON file
        /// </summary>
        /// <param name="path">Path to the settings file</param>
        /// <returns>AppSettings object</returns>
        public static AppSettings LoadJson(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                
                AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json, options);
                
                if (settings == null)
                {
                    throw new JsonException("Failed to deserialize settings. The JSON content may be invalid.");
                }
                
                // Validate required properties or apply defaults if needed
                ValidateSettings(settings);
                
                return settings;
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException)
            {
                Log.Error(ex, "Failed to load settings from {Path}. Error: {Error}", path, ex.Message);
                throw;
            }
        }
        
        /// <summary>
        /// Gets the appropriate settings file path based on the current environment
        /// </summary>
        /// <returns>Full path to the settings file</returns>
        public static string GetSettingsPath()
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
            return settingsPath;
        }
        
        /// <summary>
        /// Creates a sample settings file at the specified path
        /// </summary>
        /// <param name="path">Path where to create the settings file</param>
        public static void CreateSampleSettingsFile(string path)
        {
            try
            {
                // Ensure directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
                
                var settings = new AppSettings
                {
                    General = new GeneralSettings 
                    { 
                        UploadInterval = "500",
                        ArchiveRetentionDays = "30",
                        AutoArchiveAfterUpload = true
                    },
                    Sftp = new SftpSettings
                    {
                        Host = "192.168.1.16",
                        Port = "22",
                        Username = "Admin",
                        Password = "1234",
                        RemoteDirectory = "/var/sftp/uploads"
                    },
                    Database = new DatabaseSettings
                    {
                        Server = "192.168.1.16,1433",
                        DatabaseName = "SysOneStatic",
                        TableName = "UploadSettings",
                        Username = "Admin",
                        Password = "1234"
                    },
                    FileSettings = new FileSettings
                    {
                        FileNamePrefix = "upload_",
                        FileNameSuffix = ".txt",
                        DateTimeFormat = "yyyyMMdd_HHmmss"
                    },
                    MessageFormat = new MessageFormatSettings
                    {
                        DecimalSeparator = ".",
                        StringFormatItems = new List<StringFormatItem>
                        {
                            new StringFormatItem { FieldType = "Weight_Ratio", FixedLength = 10, DecimalPlaces = 2, Position = 1 },
                            new StringFormatItem { FieldType = "LiquidVolume", FixedLength = 9, DecimalPlaces = 2, Position = 2 },
                            new StringFormatItem { FieldType = "Barcode", FixedLength = 12, Position = 3 },
                            new StringFormatItem { FieldType = "Custom", CustomValue = " ", FixedLength = 9, Position = 4 },
                            new StringFormatItem { FieldType = "Length", FixedLength = 10, DecimalPlaces = 0, Prefix_Char = 2, Position = 5 },
                            new StringFormatItem { FieldType = "Width", FixedLength = 10, DecimalPlaces = 2, Position = 6 },
                            new StringFormatItem { FieldType = "Height", FixedLength = 10, DecimalPlaces = 0, Position = 7 }
                        }
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
        
        /// <summary>
        /// Validates the settings and ensures required properties are set
        /// </summary>
        /// <param name="settings">Settings to validate</param>
        public static void ValidateSettings(AppSettings settings)
        {
            // Initialize General settings if not present
            if (settings.General == null)
            {
                Log.Warning("General settings are not specified. Using default General settings.");
                settings.General = new GeneralSettings 
                { 
                    UploadInterval = "500",
                    ArchiveRetentionDays = "30",
                    AutoArchiveAfterUpload = true
                };
            }
            else
            {
                // Validate and set defaults for missing settings
                if (string.IsNullOrEmpty(settings.General.ArchiveRetentionDays))
                {
                    Log.Information("ArchiveRetentionDays not specified. Using default 30 days");
                    settings.General.ArchiveRetentionDays = "30";
                }
                
                Log.Information("Archive settings: Retention={RetentionDays} days, AutoArchive={AutoArchive}", 
                    settings.General.GetArchiveRetentionDays(), settings.General.AutoArchiveAfterUpload);
            }
            
            // Initialize SFTP settings if not present
            if (settings.Sftp == null)
            {
                Log.Warning("SFTP settings are not specified. Using default SFTP settings.");
                settings.Sftp = new SftpSettings
                {
                    Host = "localhost",
                    Port = "22",
                    Username = "user",
                    Password = "password",
                    RemoteDirectory = "/uploads"
                };
            }
            
            // Initialize Database settings if not present
            if (settings.Database == null)
            {
                Log.Warning("Database settings are not specified. Using default Database settings.");
                settings.Database = new DatabaseSettings
                {
                    Server = "localhost",
                    DatabaseName = "SysOneStatic",
                    TableName = "UploadSettings",
                    Username = "sa",
                    Password = "password"
                };
            }
            
            // Initialize FileSettings if not present
            if (settings.FileSettings == null)
            {
                Log.Warning("File settings are not specified. Using default File settings.");
                settings.FileSettings = new FileSettings
                {
                    FileNamePrefix = "upload_",
                    FileNameSuffix = ".txt",
                    DateTimeFormat = "yyyyMMdd_HHmmss"
                };
            }
            else
            {
                // Validate DateTimeFormat if specified
                if (!string.IsNullOrEmpty(settings.FileSettings.DateTimeFormat) && !settings.FileSettings.IsDateTimeFormatValid())
                {
                    Log.Warning("Invalid DateTimeFormat '{InvalidFormat}' specified. Using default 'yyyyMMdd_HHmmss'", settings.FileSettings.DateTimeFormat);
                    settings.FileSettings.DateTimeFormat = "yyyyMMdd_HHmmss";
                }
                else if (string.IsNullOrEmpty(settings.FileSettings.DateTimeFormat))
                {
                    Log.Information("DateTimeFormat not specified. Using default 'yyyyMMdd_HHmmss'");
                    settings.FileSettings.DateTimeFormat = "yyyyMMdd_HHmmss";
                }
                else
                {
                    Log.Information("Using DateTimeFormat: '{DateTimeFormat}' (Example: {Example})", 
                        settings.FileSettings.DateTimeFormat, 
                        settings.FileSettings.GetDateTimeFormatExample());
                }
            }
            
            // Initialize MessageFormat if not present
            if (settings.MessageFormat == null)
            {
                Log.Warning("MessageFormat is not specified in settings. Using default MessageFormat settings.");
                settings.MessageFormat = new MessageFormatSettings
                {
                    DecimalSeparator = ".",
                    StringFormatItems = new List<StringFormatItem>()
                };
            }
            else if (string.IsNullOrEmpty(settings.MessageFormat.DecimalSeparator))
            {
                Log.Information("DecimalSeparator not specified. Using default decimal separator '.'");
                settings.MessageFormat.DecimalSeparator = ".";
            }
        }
    }
}