using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using Systems_One_Sftp_Upload_Service.Models;

namespace Systems_One_Sftp_Upload_Service
{
    /// <summary>
    /// Advanced message formatter that handles complex data formatting scenarios
    /// </summary>
    public class MessageFormatter
    {
        private readonly MessageFormatSettings _formatSettings;
        private readonly ILogger<MessageFormatter> _logger;

        /// <summary>
        /// Initializes a new instance of the MessageFormatter class
        /// </summary>
        /// <param name="formatSettings">Message format settings</param>
        /// <param name="logger">Logger instance</param>
        public MessageFormatter(MessageFormatSettings formatSettings, ILogger<MessageFormatter> logger)
        {
            _formatSettings = formatSettings ?? throw new ArgumentNullException(nameof(formatSettings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Formats a single data record into a message string
        /// </summary>
        /// <param name="data">Dictionary containing field values</param>
        /// <returns>Formatted message string</returns>
        public string FormatMessage(Dictionary<string, object> data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            try
            {
                _logger.LogDebug("Formatting message with {FieldCount} fields", data.Count);
                
                var result = _formatSettings.FormatMessage(data);
                
                _logger.LogDebug("Successfully formatted message: Length={Length}", result.Length);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error formatting message with data: {@Data}", data);
                throw;
            }
        }

        /// <summary>
        /// Formats multiple data records into a multi-line message string
        /// </summary>
        /// <param name="dataList">List of data dictionaries to format</param>
        /// <returns>Multi-line formatted message string</returns>
        public string FormatMessages(IEnumerable<Dictionary<string, object>> dataList)
        {
            if (dataList == null)
                throw new ArgumentNullException(nameof(dataList));

            var messages = new List<string>();
            int recordCount = 0;

            try
            {
                foreach (var data in dataList)
                {
                    recordCount++;
                    var formattedMessage = FormatMessage(data);
                    messages.Add(formattedMessage);
                }

                var result = string.Join(Environment.NewLine, messages);
                _logger.LogInformation("Successfully formatted {RecordCount} messages", recordCount);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error formatting messages at record {RecordNumber}", recordCount);
                throw;
            }
        }

        /// <summary>
        /// Validates that the provided data contains all required fields
        /// </summary>
        /// <param name="data">Data dictionary to validate</param>
        /// <returns>Validation result with details</returns>
        public ValidationResult ValidateData(Dictionary<string, object> data)
        {
            var result = new ValidationResult();

            if (data == null)
            {
                result.AddError("Data cannot be null");
                return result;
            }

            if (_formatSettings.StringFormatItems == null || !_formatSettings.StringFormatItems.Any())
            {
                result.AddWarning("No format items defined in settings");
                return result;
            }

            foreach (var item in _formatSettings.StringFormatItems)
            {
                // Skip validation for Custom fields as they use CustomValue
                if (item.FieldType == "Custom" || string.IsNullOrEmpty(item.FieldType))
                    continue;

                if (!data.ContainsKey(item.FieldType))
                {
                    result.AddError($"Missing required field: {item.FieldType}");
                    continue;
                }

                var value = data[item.FieldType];
                
                // Validate numeric fields
                if (item.DecimalPlaces.HasValue)
                {
                    if (!IsNumericType(value))
                    {
                        result.AddError($"Field {item.FieldType} should be numeric but got {value?.GetType().Name ?? "null"}");
                    }
                }

                // Validate field length if specified
                if (item.FixedLength.HasValue)
                {
                    var formattedValue = item.FormatValue(value);
                    if (formattedValue.Length > item.FixedLength.Value)
                    {
                        result.AddWarning($"Field {item.FieldType} value '{formattedValue}' exceeds fixed length {item.FixedLength.Value}");
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Creates sample data based on the format settings for testing purposes
        /// </summary>
        /// <returns>Dictionary with sample values</returns>
        public Dictionary<string, object> CreateSampleData()
        {
            var sampleData = new Dictionary<string, object>();

            if (_formatSettings.StringFormatItems == null)
                return sampleData;

            foreach (var item in _formatSettings.StringFormatItems.Where(i => !string.IsNullOrEmpty(i.FieldType) && i.FieldType != "Custom"))
            {
                sampleData[item.FieldType!] = GenerateSampleValue(item);
            }

            _logger.LogDebug("Created sample data with {FieldCount} fields", sampleData.Count);
            return sampleData;
        }

        /// <summary>
        /// Gets formatting statistics for the current settings
        /// </summary>
        /// <returns>Formatting statistics</returns>
        public FormattingStats GetFormattingStats()
        {
            var stats = new FormattingStats();

            if (_formatSettings.StringFormatItems == null)
                return stats;

            stats.TotalFields = _formatSettings.StringFormatItems.Count;
            stats.CustomFields = _formatSettings.StringFormatItems.Count(i => i.FieldType == "Custom");
            stats.NumericFields = _formatSettings.StringFormatItems.Count(i => i.DecimalPlaces.HasValue);
            stats.FixedLengthFields = _formatSettings.StringFormatItems.Count(i => i.FixedLength.HasValue);
            stats.IsDelimited = _formatSettings.IsDelimited;
            stats.ExpectedMessageLength = _formatSettings.StringFormatItems.Sum(i => i.FixedLength ?? 0);

            return stats;
        }

        /// <summary>
        /// Demonstrates padding behavior with various field types
        /// </summary>
        /// <returns>A formatted demonstration message</returns>
        public string CreatePaddingDemo()
        {
            var demoData = new Dictionary<string, object>
            {
                {"Weight_Ratio", 0.08m},
                {"LiquidVolume", 131.31m},
                {"Barcode", "ABC123"},
                {"Length", 290},
                {"Width", 2.80m},
                {"Height", 164}
            };

            var result = FormatMessage(demoData);
            
            _logger.LogInformation("=== Padding Demo ===");
            _logger.LogInformation("Input values:");
            foreach (var kvp in demoData)
            {
                _logger.LogInformation("  {Field}: {Value}", kvp.Key, kvp.Value);
            }
            _logger.LogInformation("Formatted result: '{Result}'", result);
            _logger.LogInformation("Expected: Numeric fields right-aligned, text fields left-aligned");
            
            // Show decimal separator info
            var decimalSeparator = _formatSettings.DecimalSeparator ?? ".";
            _logger.LogInformation("Using decimal separator: '{DecimalSeparator}'", decimalSeparator);
            
            return result;
        }

        /// <summary>
        /// Demonstrates decimal separator functionality with different separators
        /// </summary>
        /// <returns>Formatted messages with different decimal separators</returns>
        public string CreateDecimalSeparatorDemo()
        {
            var demoData = new Dictionary<string, object>
            {
                {"Weight_Ratio", 12.34m},
                {"LiquidVolume", 567.89m},
                {"Width", 123.45m}
            };

            _logger.LogInformation("=== Decimal Separator Demo ===");
            _logger.LogInformation("Current decimal separator: '{Separator}'", _formatSettings.DecimalSeparator ?? ".");
            
            var result = FormatMessage(demoData);
            _logger.LogInformation("Formatted with current separator: '{Result}'", result);
            
            return result;
        }

        /// <summary>
        /// Comprehensive test to demonstrate decimal separator functionality
        /// </summary>
        /// <param name="customSeparator">Optional custom separator to test with</param>
        /// <returns>Test results as a formatted string</returns>
        public string TestDecimalSeparatorFunctionality(string? customSeparator = null)
        {
            var testResults = new StringBuilder();
            testResults.AppendLine("=== Decimal Separator Functionality Test ===");
            
            // Test data with various decimal values
            var testValues = new Dictionary<string, decimal>
            {
                {"Small decimal", 0.08m},
                {"Medium decimal", 123.45m},
                {"Large decimal", 9876.54m},
                {"Integer", 100m},
                {"High precision", 3.14159m}
            };
            
            // Original separator
            var originalSeparator = _formatSettings.DecimalSeparator;
            testResults.AppendLine($"Original decimal separator: '{originalSeparator}'");
            
            // Test with custom separator if provided
            if (!string.IsNullOrEmpty(customSeparator))
            {
                // Temporarily change the separator for testing
                _formatSettings.DecimalSeparator = customSeparator;
                testResults.AppendLine($"Testing with custom separator: '{customSeparator}'");
            }
            
            testResults.AppendLine("\nTest results:");
            foreach (var kvp in testValues)
            {
                // Create a test format item with 2 decimal places and 15 character width
                var testItem = new StringFormatItem
                {
                    FieldType = "Test",
                    FixedLength = 15,
                    DecimalPlaces = 2,
                    Position = 1
                };
                
                var formatted = testItem.FormatValue(kvp.Value, _formatSettings.DecimalSeparator);
                testResults.AppendLine($"  {kvp.Key}: {kvp.Value} ? '{formatted}'");
            }
            
            // Restore original separator
            _formatSettings.DecimalSeparator = originalSeparator;
            
            testResults.AppendLine("=== End Test ===");
            
            var result = testResults.ToString();
            _logger.LogInformation(result);
            return result;
        }

        /// <summary>
        /// Generates a sample value for a format item based on its type and constraints
        /// </summary>
        private object GenerateSampleValue(StringFormatItem item)
        {
            return item.FieldType switch
            {
                "Weight_Ratio" => GenerateWeightRatioSample(item),
                "LiquidVolume" => item.DecimalPlaces.HasValue ? 56.78m : 57,
                "Barcode" => GenerateSampleString("ABC123456789", item.FixedLength),
                "Length" => item.DecimalPlaces.HasValue ? 100.00m : 100,
                "Width" => item.DecimalPlaces.HasValue ? 45.67m : 46,
                "Height" => item.DecimalPlaces.HasValue ? 89.12m : 89,
                "Product_Code" => GenerateSampleString("PROD001", item.FixedLength),
                "Timestamp" => DateTime.Now,
                "Batch_Number" => GenerateSampleString("BTH001", item.FixedLength),
                _ => item.DecimalPlaces.HasValue ? 123.45m : $"Sample_{item.FieldType}"
            };
        }

        /// <summary>
        /// Generates a realistic Weight_Ratio sample value using the calculation formula
        /// </summary>
        private decimal GenerateWeightRatioSample(StringFormatItem item)
        {
            // Use realistic sample dimensions for calculation
            decimal sampleLength = 100.00m;  // mm
            decimal sampleWidth = 45.67m;    // mm
            decimal sampleHeight = 89.12m;   // mm
            decimal sampleWeight = 350m;     // grams
            
            // Calculate Weight_Ratio using the formula: ((L*W*H)/10000) - (weight/1000)
            var volumeBasedWeight = (sampleLength * sampleWidth * sampleHeight) / 10000m;
            var actualWeightInKg = sampleWeight / 1000m;
            var weightRatio = volumeBasedWeight - actualWeightInKg;
            
            _logger.LogDebug("Generated Weight_Ratio sample: ({L}*{W}*{H})/10000 - {Weight}/1000 = {WeightRatio}", 
                sampleLength, sampleWidth, sampleHeight, sampleWeight, weightRatio);
            
            return weightRatio;
        }

        /// <summary>
        /// Generates a sample string value with appropriate length
        /// </summary>
        private string GenerateSampleString(string baseValue, int? maxLength)
        {
            if (!maxLength.HasValue)
                return baseValue;

            if (baseValue.Length >= maxLength.Value)
                return baseValue.Substring(0, maxLength.Value);

            // For string samples, use right padding (left padding is for numeric values)
            return baseValue.PadRight(maxLength.Value, 'X');
        }

        /// <summary>
        /// Checks if a value is of a numeric type
        /// </summary>
        private static bool IsNumericType(object? value)
        {
            return value is decimal or double or float or int or long or short or byte or sbyte;
        }
    }

    /// <summary>
    /// Represents the result of data validation
    /// </summary>
    public class ValidationResult
    {
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();
        public bool IsValid => !Errors.Any();

        public void AddError(string message) => Errors.Add(message);
        public void AddWarning(string message) => Warnings.Add(message);

        public override string ToString()
        {
            var sb = new StringBuilder();
            if (Errors.Any())
            {
                sb.AppendLine($"Errors ({Errors.Count}):");
                foreach (var error in Errors)
                    sb.AppendLine($"  - {error}");
            }
            if (Warnings.Any())
            {
                sb.AppendLine($"Warnings ({Warnings.Count}):");
                foreach (var warning in Warnings)
                    sb.AppendLine($"  - {warning}");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Contains statistics about the formatting configuration
    /// </summary>
    public class FormattingStats
    {
        public int TotalFields { get; set; }
        public int CustomFields { get; set; }
        public int NumericFields { get; set; }
        public int FixedLengthFields { get; set; }
        public bool IsDelimited { get; set; }
        public int ExpectedMessageLength { get; set; }

        public override string ToString()
        {
            return $"Fields: {TotalFields} (Custom: {CustomFields}, Numeric: {NumericFields}, Fixed: {FixedLengthFields}), " +
                   $"Format: {(IsDelimited ? "Delimited" : "Fixed Width")}, Expected Length: {ExpectedMessageLength}";
        }
    }
}