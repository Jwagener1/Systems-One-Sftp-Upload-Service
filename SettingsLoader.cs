using System;
using System.IO;
using Newtonsoft.Json;
using Serilog;

namespace Systems_One_Sftp_Upload_Service
{
    public class Settings
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

    public static class SettingsLoader
    {
        /// <summary>
        /// Loads settings from a JSON file
        /// </summary>
        /// <param name="path">Path to the settings file</param>
        /// <returns>Settings object</returns>
        public static Settings LoadJson(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                Settings? settings = JsonConvert.DeserializeObject<Settings>(json);
                
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
        /// Validates the settings and ensures required properties are set
        /// </summary>
        /// <param name="settings">Settings to validate</param>
        private static void ValidateSettings(Settings settings)
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
            
            // Ensure directories exist
            try
            {
                Directory.CreateDirectory(settings.UploadDirectory);
                Directory.CreateDirectory(settings.ArchiveDirectory);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to create directories. Error: {Error}", ex.Message);
            }
        }
    }
}
