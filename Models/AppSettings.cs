using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Systems_One_Sftp_Upload_Service.Models
{
    /// <summary>
    /// Main settings class for the SFTP Upload Service
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// General application settings
        /// </summary>
        public GeneralSettings? General { get; set; }
        
        /// <summary>
        /// SFTP connection settings
        /// </summary>
        public SftpSettings? Sftp { get; set; }
        
        /// <summary>
        /// Database connection settings
        /// </summary>
        public DatabaseSettings? Database { get; set; }
        
        /// <summary>
        /// File naming settings
        /// </summary>
        public FileSettings? FileSettings { get; set; }
        
        /// <summary>
        /// Message format settings
        /// </summary>
        public MessageFormatSettings? MessageFormat { get; set; }
    }

    /// <summary>
    /// General application settings
    /// </summary>
    public class GeneralSettings
    {
        /// <summary>
        /// Interval for upload tasks in milliseconds
        /// </summary>
        public string? UploadInterval { get; set; }
        
        /// <summary>
        /// Gets the upload interval as an integer
        /// </summary>
        public int GetUploadIntervalMs()
        {
            if (int.TryParse(UploadInterval, out int interval))
                return interval;
            
            // Default to 500ms if parsing fails
            return 500;
        }
    }

    /// <summary>
    /// SFTP connection settings
    /// </summary>
    public class SftpSettings
    {
        /// <summary>
        /// SFTP server hostname or IP address
        /// </summary>
        public string? Host { get; set; }
        
        /// <summary>
        /// SFTP server port
        /// </summary>
        public string? Port { get; set; }
        
        /// <summary>
        /// SFTP username
        /// </summary>
        public string? Username { get; set; }
        
        /// <summary>
        /// SFTP password
        /// </summary>
        public string? Password { get; set; }
        
        /// <summary>
        /// Remote directory on the SFTP server
        /// </summary>
        public string? RemoteDirectory { get; set; }
        
        /// <summary>
        /// Gets the port as an integer
        /// </summary>
        public int GetPort()
        {
            if (int.TryParse(Port, out int port))
                return port;
            
            // Default to standard SFTP port if parsing fails
            return 22;
        }
    }

    /// <summary>
    /// Database connection settings
    /// </summary>
    public class DatabaseSettings
    {
        /// <summary>
        /// Database server hostname or IP address
        /// </summary>
        public string? Server { get; set; }
        
        /// <summary>
        /// Database name
        /// </summary>
        public string? DatabaseName { get; set; }
        
        /// <summary>
        /// Table name
        /// </summary>
        public string? TableName { get; set; }
        
        /// <summary>
        /// Database username
        /// </summary>
        public string? Username { get; set; }
        
        /// <summary>
        /// Database password
        /// </summary>
        public string? Password { get; set; }
        
        /// <summary>
        /// Gets a properly formatted connection string
        /// </summary>
        public string GetConnectionString()
        {
            return $"Server={Server};Database={DatabaseName};User Id={Username};Password={Password};";
        }
    }

    /// <summary>
    /// File naming settings
    /// </summary>
    public class FileSettings
    {
        /// <summary>
        /// Prefix for the generated file name
        /// </summary>
        public string? FileNamePrefix { get; set; }
        
        /// <summary>
        /// Suffix for the generated file name (usually file extension)
        /// </summary>
        public string? FileNameSuffix { get; set; }
        
        /// <summary>
        /// Generates a complete file name with date and time
        /// </summary>
        public string GenerateFileName(DateTime? dateTime = null)
        {
            dateTime ??= DateTime.Now;
            
            return $"{FileNamePrefix ?? "upload_"}{dateTime:yyyyMMdd_HHmmss}{FileNameSuffix ?? ".txt"}";
        }
    }

    /// <summary>
    /// Message format settings
    /// </summary>
    public class MessageFormatSettings
    {
        /// <summary>
        /// Delimiter character between fields (null for fixed width)
        /// </summary>
        public string? MessageDelimiter { get; set; }
        
        /// <summary>
        /// Start character for the message
        /// </summary>
        public string? MessageStartCharacter { get; set; }
        
        /// <summary>
        /// End character for the message
        /// </summary>
        public string? MessageEndCharacter { get; set; }
        
        /// <summary>
        /// List of format items defining the message structure
        /// </summary>
        public List<StringFormatItem>? StringFormatItems { get; set; }
        
        /// <summary>
        /// Indicates if this is a delimited format or fixed width
        /// </summary>
        public bool IsDelimited => !string.IsNullOrEmpty(MessageDelimiter);
        
        /// <summary>
        /// Format a message using the defined format items
        /// </summary>
        public string FormatMessage(Dictionary<string, object> values)
        {
            if (StringFormatItems == null || StringFormatItems.Count == 0)
                return string.Empty;
                
            var formattedItems = new List<string>();
            
            // Sort format items by position
            var sortedItems = StringFormatItems.OrderBy(i => i.Position).ToList();
            
            foreach (var item in sortedItems)
            {
                string formattedValue;
                
                // Use custom value if field type is Custom
                if (item.FieldType == "Custom")
                {
                    formattedValue = item.FormatValue(item.CustomValue);
                }
                // Try to get the value from the dictionary
                else if (item.FieldType != null && values.TryGetValue(item.FieldType, out var value))
                {
                    formattedValue = item.FormatValue(value);
                }
                // Use empty space if no value found
                else
                {
                    formattedValue = item.FormatValue(null);
                }
                
                formattedItems.Add(formattedValue);
            }
            
            // Join items with delimiter or empty string for fixed width
            string delimiter = MessageDelimiter ?? "";
            string message = string.Join(delimiter, formattedItems);
            
            // Add start and end characters if specified
            if (!string.IsNullOrEmpty(MessageStartCharacter))
                message = MessageStartCharacter + message;
                
            if (!string.IsNullOrEmpty(MessageEndCharacter))
                message = message + MessageEndCharacter;
                
            return message;
        }
    }

    /// <summary>
    /// Individual format item for a message field
    /// </summary>
    public class StringFormatItem
    {
        /// <summary>
        /// Type of field (Weight_Ratio, LiquidVolume, Barcode, Custom, etc.)
        /// </summary>
        public string? FieldType { get; set; }
        
        /// <summary>
        /// Custom value for Custom field type
        /// </summary>
        public string? CustomValue { get; set; }
        
        /// <summary>
        /// Fixed length of the field
        /// </summary>
        public int? FixedLength { get; set; }
        
        /// <summary>
        /// Number of decimal places for numeric fields
        /// </summary>
        public int? DecimalPlaces { get; set; }
        
        /// <summary>
        /// Prefix character for the field
        /// </summary>
        public object? Prefix_Char { get; set; }
        
        /// <summary>
        /// Position of the field in the message
        /// </summary>
        public int Position { get; set; }
        
        /// <summary>
        /// Gets the prefix character as a string
        /// </summary>
        public string? GetPrefixChar()
        {
            if (Prefix_Char == null)
                return null;
                
            // Handle the case where Prefix_Char might be a number or a string
            return Prefix_Char.ToString();
        }
        
        /// <summary>
        /// Format a value according to the field type specifications
        /// </summary>
        public string FormatValue(object? value)
        {
            // Handle null value
            if (value == null)
                return new string(' ', FixedLength ?? 0);
                
            // For custom values, return the custom value
            if (FieldType == "Custom" && CustomValue != null)
                return PadToLength(CustomValue);
                
            // For numeric values, format with decimal places
            if (value is decimal decimalValue && DecimalPlaces.HasValue)
            {
                string format = $"F{DecimalPlaces.Value}";
                string formattedValue = decimalValue.ToString(format);
                return PadToLength(GetPrefixChar() + formattedValue);
            }
            
            // Default string formatting
            return PadToLength(GetPrefixChar() + value.ToString());
        }
        
        /// <summary>
        /// Pads the string to the fixed length if specified
        /// </summary>
        private string PadToLength(string? input)
        {
            if (input == null)
                input = string.Empty;
                
            if (FixedLength.HasValue)
                return input.PadRight(FixedLength.Value);
                
            return input;
        }
    }
}