using System;
using System.Collections.Generic;
using Serilog;
using Systems_One_Sftp_Upload_Service.Models;

namespace Systems_One_Sftp_Upload_Service
{
    /// <summary>
    /// Utility class for formatting strings based on the message format settings
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
            _logger = logger ?? Log.Logger;
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
                // Use the FormatMessage method from MessageFormatSettings
                return _formatSettings.FormatMessage(data);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error formatting data: {Error}", ex.Message);
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

            foreach (var item in _formatSettings.StringFormatItems)
            {
                if (item.FieldType == null || item.FieldType == "Custom")
                    continue;

                // Create appropriate sample data based on field type
                switch (item.FieldType)
                {
                    case "Weight_Ratio":
                        sampleData[item.FieldType] = 12.34m;
                        break;
                    case "LiquidVolume":
                        sampleData[item.FieldType] = 56.78m;
                        break;
                    case "Barcode":
                        sampleData[item.FieldType] = "ABC123456789";
                        break;
                    case "Length":
                        sampleData[item.FieldType] = 100;
                        break;
                    case "Width":
                        sampleData[item.FieldType] = 45.67m;
                        break;
                    case "Height":
                        sampleData[item.FieldType] = 89;
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
                    _logger.Warning("Missing required field {FieldType} in data", item.FieldType);
                    return false;
                }

                // Validate numeric fields have appropriate values
                if (item.DecimalPlaces.HasValue)
                {
                    if (!(data[item.FieldType] is decimal || data[item.FieldType] is int || data[item.FieldType] is double || data[item.FieldType] is float))
                    {
                        _logger.Warning("Field {FieldType} should be a numeric value", item.FieldType);
                        return false;
                    }
                }
            }

            return true;
        }
    }
}