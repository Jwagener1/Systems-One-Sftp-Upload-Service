using System;
using System.Collections.Generic;
using System.Linq;
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
        /// Number of days to keep archived files before deletion
        /// </summary>
        public string? ArchiveRetentionDays { get; set; }
        
        /// <summary>
        /// Whether to automatically archive files after successful upload
        /// </summary>
        public bool AutoArchiveAfterUpload { get; set; } = true;
        
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
        
        /// <summary>
        /// Gets the archive retention period in days
        /// </summary>
        public int GetArchiveRetentionDays()
        {
            if (int.TryParse(ArchiveRetentionDays, out int days))
                return days;
            
            // Default to 30 days if parsing fails
            return 30;
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
        /// Whether to trust the server certificate (useful for development environments with self-signed certificates)
        /// </summary>
        public bool? TrustServerCertificate { get; set; }
        
        /// <summary>
        /// Connection timeout in seconds
        /// </summary>
        public int? ConnectionTimeoutSeconds { get; set; }
        
        /// <summary>
        /// Gets a properly formatted connection string with SSL certificate trust settings
        /// </summary>
        public string GetConnectionString()
        {
            var connectionStringBuilder = new System.Text.StringBuilder();
            connectionStringBuilder.Append($"Server={Server};");
            connectionStringBuilder.Append($"Database={DatabaseName};");
            connectionStringBuilder.Append($"User Id={Username};");
            connectionStringBuilder.Append($"Password={Password};");
            
            // Add SSL certificate trust setting (default to true for compatibility)
            bool trustCert = TrustServerCertificate ?? true;
            connectionStringBuilder.Append($"TrustServerCertificate={trustCert.ToString().ToLower()};");
            
            // Add connection timeout (default to 30 seconds)
            int timeout = ConnectionTimeoutSeconds ?? 30;
            connectionStringBuilder.Append($"Connection Timeout={timeout};");
            
            return connectionStringBuilder.ToString();
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
        /// Date-time format pattern for the file name (e.g., "yyyyMMdd_HHmmss", "yyyy-MM-dd_HH-mm-ss")
        /// If null or empty, defaults to "yyyyMMdd_HHmmss"
        /// </summary>
        public string? DateTimeFormat { get; set; }
        
        /// <summary>
        /// Gets the date-time format to use, with fallback to default
        /// </summary>
        public string GetDateTimeFormat()
        {
            return !string.IsNullOrEmpty(DateTimeFormat) ? DateTimeFormat : "yyyyMMdd_HHmmss";
        }
        
        /// <summary>
        /// Generates a complete file name with date and time
        /// </summary>
        public string GenerateFileName(DateTime? dateTime = null)
        {
            dateTime ??= DateTime.Now;
            
            string dateTimeString;
            try
            {
                dateTimeString = dateTime.Value.ToString(GetDateTimeFormat());
            }
            catch (FormatException)
            {
                // If custom format is invalid, fall back to default
                dateTimeString = dateTime.Value.ToString("yyyyMMdd_HHmmss");
                // Note: Can't use logger here as this is a model class, but the calling code should handle this
            }
            
            return $"{FileNamePrefix ?? "upload_"}{dateTimeString}{FileNameSuffix ?? ".txt"}";
        }
        
        /// <summary>
        /// Validates the DateTimeFormat pattern
        /// </summary>
        /// <returns>True if the format is valid, false otherwise</returns>
        public bool IsDateTimeFormatValid()
        {
            if (string.IsNullOrEmpty(DateTimeFormat))
                return true; // null/empty is valid (uses default)
                
            try
            {
                DateTime.Now.ToString(DateTimeFormat);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
        
        /// <summary>
        /// Gets example output for the current DateTimeFormat
        /// </summary>
        /// <param name="sampleDateTime">Optional sample date to use for example</param>
        /// <returns>Example formatted string</returns>
        public string GetDateTimeFormatExample(DateTime? sampleDateTime = null)
        {
            var sample = sampleDateTime ?? new DateTime(2025, 7, 22, 7, 39, 3);
            try
            {
                return sample.ToString(GetDateTimeFormat());
            }
            catch (FormatException)
            {
                return $"Invalid format: {DateTimeFormat}";
            }
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
        /// Decimal separator character for numeric formatting (e.g., "." or ",")
        /// </summary>
        public string? DecimalSeparator { get; set; }
        
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
                    formattedValue = item.FormatValue(item.CustomValue, DecimalSeparator);
                }
                // Try to get the value from the dictionary
                else if (item.FieldType != null && values.TryGetValue(item.FieldType, out var value))
                {
                    formattedValue = item.FormatValue(value, DecimalSeparator);
                }
                // Use empty space if no value found
                else
                {
                    formattedValue = item.FormatValue(null, DecimalSeparator);
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
        /// <param name="value">The value to format</param>
        /// <param name="decimalSeparator">Custom decimal separator (optional)</param>
        public string FormatValue(object? value, string? decimalSeparator = null)
        {
            // Handle null value
            if (value == null)
                return new string(' ', FixedLength ?? 0);
                
            // For custom values, return the custom value
            if (FieldType == "Custom" && CustomValue != null)
                return PadToLength(CustomValue, isNumeric: false);
            
            string valueString;
            string prefixChar = GetPrefixChar() ?? "";
            bool isNumericField = DecimalPlaces.HasValue;
            
            // Handle different numeric types with decimal places
            if (DecimalPlaces.HasValue)
            {
                decimal decimalValue = 0;
                
                // Convert various numeric types to decimal
                switch (value)
                {
                    case decimal d:
                        decimalValue = d;
                        break;
                    case double dbl:
                        decimalValue = (decimal)dbl;
                        break;
                    case float f:
                        decimalValue = (decimal)f;
                        break;
                    case int i:
                        decimalValue = i;
                        break;
                    case long l:
                        decimalValue = l;
                        break;
                    case short s:
                        decimalValue = s;
                        break;
                    default:
                        // Try to parse as decimal
                        if (decimal.TryParse(value.ToString(), out decimal parsed))
                            decimalValue = parsed;
                        else
                            return PadToLength(prefixChar + value.ToString(), isNumeric: false);
                        break;
                }
                
                string format = $"F{DecimalPlaces.Value}";
                valueString = decimalValue.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
                
                // Apply custom decimal separator if specified
                if (!string.IsNullOrEmpty(decimalSeparator) && decimalSeparator != ".")
                {
                    valueString = valueString.Replace(".", decimalSeparator);
                }
            }
            else
            {
                // Default string formatting for non-decimal values
                valueString = value.ToString() ?? "";
                // Check if it's a numeric type even without DecimalPlaces specified
                isNumericField = IsNumericType(value);
            }
            
            return PadToLength(prefixChar + valueString, isNumeric: isNumericField);
        }
        
        /// <summary>
        /// Checks if a value is of a numeric type
        /// </summary>
        private static bool IsNumericType(object? value)
        {
            return value is decimal or double or float or int or long or short or byte or sbyte;
        }
        
        /// <summary>
        /// Pads the string to the fixed length if specified
        /// </summary>
        /// <param name="input">Input string to pad</param>
        /// <param name="isNumeric">True if this is a numeric field (use left padding), false for text fields (use right padding)</param>
        private string PadToLength(string? input, bool isNumeric = false)
        {
            if (input == null)
                input = string.Empty;
                
            if (FixedLength.HasValue)
            {
                if (input.Length > FixedLength.Value)
                {
                    // Truncate if too long
                    return input.Substring(0, FixedLength.Value);
                }
                else
                {
                    // Use left padding for numeric fields, right padding for text fields
                    if (isNumeric)
                        return input.PadLeft(FixedLength.Value);
                    else
                        return input.PadRight(FixedLength.Value);
                }
            }
                
            return input;
        }
    }
}