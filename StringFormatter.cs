using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Systems_One_Sftp_Upload_Service.Models;

namespace Systems_One_Sftp_Upload_Service
{
    /// <summary>
    /// Legacy utility class for formatting strings - maintained for backward compatibility
    /// Consider using MessageFormatter for new implementations
    /// </summary>
    public class StringFormatter
    {
        private readonly MessageFormatSettings _formatSettings;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the StringFormatter class
        /// </summary>
        /// <param name="formatSettings">Message format settings</param>
        /// <param name="logger">Logger instance</param>
        public StringFormatter(MessageFormatSettings formatSettings, ILogger logger)
        {
            _formatSettings = formatSettings ?? throw new ArgumentNullException(nameof(formatSettings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Formats a data object according to the defined message format settings
        /// </summary>
        /// <param name="data">Dictionary of field values where keys match the FieldType in StringFormatItems</param>
        /// <returns>A formatted string</returns>
        public string FormatData(Dictionary<string, object> data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            try
            {
                return _formatSettings.FormatMessage(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error formatting data");
                throw;
            }
        }

        /// <summary>
        /// Creates a sample data dictionary with all defined field types in the format settings
        /// </summary>
        /// <returns>A dictionary with sample values for testing</returns>
        public Dictionary<string, object> CreateSampleData()
        {
            var sampleData = new Dictionary<string, object>();

            if (_formatSettings.StringFormatItems == null)
                return sampleData;
            
            // Sample dimensions for Weight_Ratio calculation
            decimal sampleLength = 100m;
            decimal sampleWidth = 45.67m;
            decimal sampleHeight = 89m;
            decimal sampleWeight = 300m; // in grams

            foreach (var item in _formatSettings.StringFormatItems)
            {
                if (item.FieldType == null || item.FieldType == "Custom")
                    continue;

                // Create appropriate sample data based on field type
                switch (item.FieldType)
                {
                    case "Weight_Ratio":
                        // Calculate Weight_Ratio using the formula: ((L*W*H)/10000) - (weight/1000)
                        var volumeBasedWeight = (sampleLength * sampleWidth * sampleHeight) / 10000m;
                        var actualWeightInKg = sampleWeight / 1000m;
                        var weightRatio = volumeBasedWeight - actualWeightInKg;
                        sampleData[item.FieldType] = weightRatio;
                        _logger.LogDebug("Sample Weight_Ratio calculation: ({Length}*{Width}*{Height})/10000 - {Weight}/1000 = {WeightRatio}", 
                            sampleLength, sampleWidth, sampleHeight, sampleWeight, weightRatio);
                        break;
                    case "LiquidVolume":
                        sampleData[item.FieldType] = 56.78m;
                        break;
                    case "Barcode":
                        sampleData[item.FieldType] = "ABC123456789";
                        break;
                    case "Length":
                        sampleData[item.FieldType] = sampleLength;
                        break;
                    case "Width":
                        sampleData[item.FieldType] = sampleWidth;
                        break;
                    case "Height":
                        sampleData[item.FieldType] = sampleHeight;
                        break;
                    default:
                        sampleData[item.FieldType] = $"Sample_{item.FieldType}";
                        break;
                }
            }

            return sampleData;
        }

        /// <summary>
        /// Formats a list of data objects into multiple lines
        /// </summary>
        /// <param name="dataList">List of data dictionaries to format</param>
        /// <returns>A string with each data item on a new line</returns>
        public string FormatDataList(List<Dictionary<string, object>> dataList)
        {
            if (dataList == null || dataList.Count == 0)
                return string.Empty;

            var formattedLines = new List<string>(dataList.Count);

            foreach (var data in dataList)
            {
                formattedLines.Add(FormatData(data));
            }

            return string.Join(Environment.NewLine, formattedLines);
        }

        /// <summary>
        /// Validates if data contains all required fields based on the format settings
        /// </summary>
        /// <param name="data">Data dictionary to validate</param>
        /// <returns>True if all required fields are present, otherwise false</returns>
        public bool ValidateData(Dictionary<string, object> data)
        {
            if (data == null)
                return false;

            if (_formatSettings.StringFormatItems == null)
                return true;

            foreach (var item in _formatSettings.StringFormatItems)
            {
                // Skip validation for Custom or null field types
                if (item.FieldType == null || item.FieldType == "Custom")
                    continue;

                if (!data.ContainsKey(item.FieldType))
                {
                    _logger.LogWarning("Missing required field {FieldType} in data", item.FieldType);
                    return false;
                }

                // Validate numeric fields have appropriate values
                if (item.DecimalPlaces.HasValue)
                {
                    if (!(data[item.FieldType] is decimal || data[item.FieldType] is int || data[item.FieldType] is double || data[item.FieldType] is float))
                    {
                        _logger.LogWarning("Field {FieldType} should be a numeric value", item.FieldType);
                        return false;
                    }
                }
            }

            return true;
        }
    }
}