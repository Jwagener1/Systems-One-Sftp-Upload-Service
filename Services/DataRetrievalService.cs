using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using Systems_One_Sftp_Upload_Service.Models;

namespace Systems_One_Sftp_Upload_Service.Services
{
    /// <summary>
    /// Interface for data retrieval services
    /// </summary>
    public interface IDataRetrievalService
    {
        /// <summary>
        /// Retrieves data records ready for upload
        /// </summary>
        /// <returns>Collection of data dictionaries with record IDs</returns>
        Task<IEnumerable<(Dictionary<string, object> Data, string RecordId)>> GetDataForUploadAsync();
        
        /// <summary>
        /// Marks data records as processed/uploaded
        /// </summary>
        /// <param name="recordIds">IDs of processed records</param>
        Task MarkRecordsAsProcessedAsync(IEnumerable<string> recordIds);
        
        /// <summary>
        /// Tests database connection
        /// </summary>
        /// <returns>True if connection successful</returns>
        Task<bool> TestConnectionAsync();
        
        /// <summary>
        /// Gets database statistics for monitoring
        /// </summary>
        /// <returns>Database statistics</returns>
        Task<(int TotalRecords, int UnsentRecords, int SentRecords, DateTime? OldestUnsent, DateTime? NewestRecord)> GetDatabaseStatisticsAsync();
    }

    /// <summary>
    /// Production implementation of data retrieval service for SQL Server
    /// Connects to SysOneStatic database and queries ItemLog table
    /// </summary>
    public class SqlServerDataRetrievalService : IDataRetrievalService
    {
        private readonly ILogger<SqlServerDataRetrievalService> _logger;
        private readonly DatabaseSettings _databaseSettings;

        public SqlServerDataRetrievalService(ILogger<SqlServerDataRetrievalService> logger, AppSettings settings)
        {
            _logger = logger;
            _databaseSettings = settings.Database ?? throw new ArgumentNullException(nameof(settings.Database));
        }

        /// <summary>
        /// Tests the database connection
        /// </summary>
        /// <returns>True if connection successful</returns>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                _logger.LogInformation("Testing database connection...");
                _logger.LogInformation("Server: {Server}, Database: {Database}", _databaseSettings.Server, _databaseSettings.DatabaseName);

                using var connection = new SqlConnection(_databaseSettings.GetConnectionString());
                await connection.OpenAsync();
                
                // Test basic query
                using var command = new SqlCommand("SELECT @@VERSION", connection);
                var version = await command.ExecuteScalarAsync();
                
                _logger.LogInformation("✓ Database connection successful");
                _logger.LogDebug("SQL Server Version: {Version}", version?.ToString());
                
                // Test if ItemLog table exists
                var tableCheckCommand = new SqlCommand(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @TableName", 
                    connection);
                tableCheckCommand.Parameters.AddWithValue("@TableName", _databaseSettings.TableName);
                
                var tableExists = Convert.ToInt32(await tableCheckCommand.ExecuteScalarAsync()) > 0;
                if (tableExists)
                {
                    _logger.LogInformation("✓ Table '{TableName}' found", _databaseSettings.TableName);
                    
                    // Get row count
                    var countCommand = new SqlCommand($"SELECT COUNT(*) FROM [{_databaseSettings.TableName}]", connection);
                    var rowCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
                    _logger.LogInformation("Table contains {RowCount} records", rowCount);
                    
                    // Check unsent records count
                    var unsentCountCommand = new SqlCommand($"SELECT COUNT(*) FROM [{_databaseSettings.TableName}] WHERE Sent = 0", connection);
                    var unsentCount = Convert.ToInt32(await unsentCountCommand.ExecuteScalarAsync());
                    _logger.LogInformation("Found {UnsentCount} unsent records ready for upload", unsentCount);
                    
                    // Check for required columns
                    await ValidateTableStructureAsync(connection);
                }
                else
                {
                    _logger.LogWarning("⚠ Table '{TableName}' not found", _databaseSettings.TableName);
                    _logger.LogInformation("Would you like the service to create the table automatically?");
                    
                    // In production, you might want to create the table automatically
                    // await CreateTableIfNotExistsAsync(connection);
                    
                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database connection test failed");
                return false;
            }
        }

        /// <summary>
        /// Validates that the table has the required columns based on your actual schema
        /// </summary>
        private async Task ValidateTableStructureAsync(SqlConnection connection)
        {
            try
            {
                // Required columns based on your database schema image
                var requiredColumns = new[] { 
                    "Id", "ItemDateTime", "Barcode", "Length", "Width", "Height", 
                    "Weight", "BoxVolume", "LiquidVolume", "NoDimension", "NoWeight", 
                    "Valid", "Sent", "ImageSent", "Complete", "ItemSpec", "ItemCount" 
                };
                
                var columnCheckQuery = @"
                    SELECT COLUMN_NAME 
                    FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = @TableName";

                using var command = new SqlCommand(columnCheckQuery, connection);
                command.Parameters.AddWithValue("@TableName", _databaseSettings.TableName);
                
                var existingColumns = new List<string>();
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    existingColumns.Add(reader["COLUMN_NAME"].ToString() ?? "");
                }
                
                var missingColumns = requiredColumns.Where(col => !existingColumns.Any(existing => 
                    string.Equals(existing, col, StringComparison.OrdinalIgnoreCase))).ToArray();
                
                if (missingColumns.Length == 0)
                {
                    _logger.LogInformation("✓ All required columns found in table");
                }
                else
                {
                    _logger.LogWarning("⚠ Missing required columns: {MissingColumns}", string.Join(", ", missingColumns));
                    _logger.LogInformation("Existing columns: {ExistingColumns}", string.Join(", ", existingColumns));
                }
                
                // Check important tracking columns
                var hasSentColumn = existingColumns.Any(col => string.Equals(col, "Sent", StringComparison.OrdinalIgnoreCase));
                var hasValidColumn = existingColumns.Any(col => string.Equals(col, "Valid", StringComparison.OrdinalIgnoreCase));
                
                if (!hasSentColumn)
                {
                    _logger.LogError("❌ Missing 'Sent' column - cannot track upload status");
                }
                if (!hasValidColumn)
                {
                    _logger.LogWarning("⚠ Missing 'Valid' column - will upload all records regardless of validity");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not validate table structure");
            }
        }

        /// <summary>
        /// Creates the ItemLog table if it doesn't exist (optional - uncomment if needed)
        /// </summary>
        /*
        private async Task CreateTableIfNotExistsAsync(SqlConnection connection)
        {
            try
            {
                _logger.LogInformation("Creating table '{TableName}'...", _databaseSettings.TableName);
                
                var createTableQuery = $@"
                    CREATE TABLE [{_databaseSettings.TableName}] (
                        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
                        Weight_Ratio DECIMAL(10,4) NULL,
                        LiquidVolume DECIMAL(10,2) NULL,
                        Barcode NVARCHAR(50) NULL,
                        Length DECIMAL(10,2) NULL,
                        Width DECIMAL(10,2) NULL,
                        Height DECIMAL(10,2) NULL,
                        CreatedDate DATETIME2 DEFAULT GETDATE(),
                        Uploaded BIT DEFAULT 0,
                        UploadedDate DATETIME2 NULL
                    )";
                
                using var command = new SqlCommand(createTableQuery, connection);
                await command.ExecuteNonQueryAsync();
                
                _logger.LogInformation("✓ Table '{TableName}' created successfully", _databaseSettings.TableName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create table");
                throw;
            }
        }
        */

        /// <summary>
        /// Retrieves the first unsent record from the ItemLog table based on your database schema
        /// </summary>
        /// <returns>Collection of data dictionaries with record IDs</returns>
        public async Task<IEnumerable<(Dictionary<string, object> Data, string RecordId)>> GetDataForUploadAsync()
        {
            var results = new List<(Dictionary<string, object> Data, string RecordId)>();
            
            try
            {
                _logger.LogInformation("Retrieving first unsent record from table: {TableName}", _databaseSettings.TableName);

                using var connection = new SqlConnection(_databaseSettings.GetConnectionString());
                await connection.OpenAsync();

                // Check if the table has the required tracking columns
                var hasSentColumn = await CheckColumnExistsAsync(connection, "Sent");
                var hasValidColumn = await CheckColumnExistsAsync(connection, "Valid");
                
                // Query for the first unsent record based on your schema
                string query;
                if (hasSentColumn && hasValidColumn)
                {
                    // Get first unsent, valid record ordered by ItemDateTime (oldest first)
                    query = $@"
                        SELECT TOP 1 
                            Id,
                            ItemDateTime,
                            Barcode,
                            Length,
                            Width, 
                            Height,
                            Weight,
                            BoxVolume,
                            LiquidVolume,
                            NoDimension,
                            NoWeight,
                            Valid,
                            Sent,
                            Complete,
                            ItemSpec,
                            ItemCount
                        FROM [{_databaseSettings.TableName}] 
                        WHERE Sent = 0 AND Valid = 1
                        ORDER BY ItemDateTime ASC, Id ASC";
                }
                else if (hasSentColumn)
                {
                    // Get first unsent record (without Valid check)
                    query = $@"
                        SELECT TOP 1 
                            Id,
                            ItemDateTime,
                            Barcode,
                            Length,
                            Width, 
                            Height,
                            Weight,
                            BoxVolume,
                            LiquidVolume,
                            NoDimension,
                            NoWeight,
                            Sent,
                            Complete,
                            ItemSpec,
                            ItemCount
                        FROM [{_databaseSettings.TableName}] 
                        WHERE Sent = 0
                        ORDER BY ItemDateTime ASC, Id ASC";
                }
                else
                {
                    // If no Sent column, get most recent record (may cause duplicates)
                    _logger.LogWarning("No 'Sent' column found - retrieving most recent record (may cause duplicates)");
                    query = $@"
                        SELECT TOP 1 
                            Id,
                            ItemDateTime,
                            Barcode,
                            Length,
                            Width, 
                            Height,
                            Weight,
                            BoxVolume,
                            LiquidVolume,
                            Complete,
                            ItemSpec,
                            ItemCount
                        FROM [{_databaseSettings.TableName}] 
                        ORDER BY ItemDateTime DESC, Id DESC";
                }

                using var command = new SqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    var recordId = reader["Id"].ToString() ?? Guid.NewGuid().ToString();
                    
                    // Get individual values for calculation
                    var length = GetSafeDecimal(reader, "Length");
                    var width = GetSafeDecimal(reader, "Width");
                    var height = GetSafeDecimal(reader, "Height");
                    var weight = GetSafeDecimal(reader, "Weight");
                    
                    // Calculate Weight_Ratio using the formula: ((L*W*H)/10000) - (weight/1000)
                    var volumeBasedWeight = (length * width * height) / 10000m;
                    var actualWeightInKg = weight / 1000m;
                    var weightRatio = volumeBasedWeight - actualWeightInKg;
                    
                    _logger.LogDebug("Weight_Ratio calculation: Length={Length}, Width={Width}, Height={Height}, Weight={Weight}", 
                        length, width, height, weight);
                    _logger.LogDebug("Volume-based weight: ({Length}*{Width}*{Height})/10000 = {VolumeWeight}", 
                        length, width, height, volumeBasedWeight);
                    _logger.LogDebug("Actual weight in kg: {Weight}/1000 = {WeightInKg}", weight, actualWeightInKg);
                    _logger.LogDebug("Calculated Weight_Ratio: {VolumeWeight} - {WeightInKg} = {WeightRatio}", 
                        volumeBasedWeight, actualWeightInKg, weightRatio);
                    
                    // Map your database fields to the message format fields
                    var data = new Dictionary<string, object>
                    {
                        // Use calculated Weight_Ratio instead of raw Weight
                        {"Weight_Ratio", weightRatio},
                        {"LiquidVolume", GetSafeDecimal(reader, "LiquidVolume")},
                        {"Barcode", GetSafeString(reader, "Barcode")},
                        {"Length", length},
                        {"Width", width},
                        {"Height", height},
                        {"RecordId", recordId},
                        // Additional fields for reference
                        {"ItemDateTime", GetSafeDateTime(reader, "ItemDateTime")},
                        {"BoxVolume", GetSafeDecimal(reader, "BoxVolume")},
                        {"NoDimension", GetSafeBit(reader, "NoDimension")},
                        {"NoWeight", GetSafeBit(reader, "NoWeight")},
                        {"Complete", GetSafeBit(reader, "Complete")},
                        {"ItemSpec", GetSafeString(reader, "ItemSpec")},
                        {"ItemCount", GetSafeInt(reader, "ItemCount")},
                        // Store raw weight for reference
                        {"RawWeight", weight}
                    };

                    results.Add((data, recordId));
                    
                    _logger.LogInformation("Retrieved 1 unsent record from database");
                    _logger.LogInformation("Record ID: {RecordId}, ItemDateTime: {ItemDateTime}, Barcode: {Barcode}", 
                        recordId, data["ItemDateTime"], data["Barcode"]);
                    
                    _logger.LogDebug("Record data details:");
                    _logger.LogDebug("  Calculated Weight_Ratio: {WeightRatio}", data["Weight_Ratio"]);
                    _logger.LogDebug("  Raw Weight: {RawWeight}", data["RawWeight"]);
                    _logger.LogDebug("  LiquidVolume: {LiquidVolume}", data["LiquidVolume"]);
                    _logger.LogDebug("  Dimensions: {Length}x{Width}x{Height}", data["Length"], data["Width"], data["Height"]);
                    _logger.LogDebug("  BoxVolume: {BoxVolume}", data["BoxVolume"]);
                    _logger.LogDebug("  Complete: {Complete}", data["Complete"]);
                }
                else
                {
                    _logger.LogInformation("No unsent records found in database");
                }
            }
            catch (SqlException sqlEx)
            {
                _logger.LogError(sqlEx, "SQL error retrieving data from database");
                _logger.LogError("SQL Error Number: {ErrorNumber}, Severity: {Severity}", sqlEx.Number, sqlEx.Class);
                
                if (sqlEx.Number == 208) // Invalid object name
                {
                    _logger.LogError("Table '{TableName}' does not exist", _databaseSettings.TableName);
                }
                else if (sqlEx.Number == 207) // Invalid column name
                {
                    _logger.LogError("One or more required columns do not exist in table '{TableName}'", _databaseSettings.TableName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving data from database");
            }

            return results;
        }

        /// <summary>
        /// Checks if a specific column exists in the table
        /// </summary>
        private async Task<bool> CheckColumnExistsAsync(SqlConnection connection, string columnName)
        {
            try
            {
                var query = @"
                    SELECT COUNT(*) 
                    FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = @TableName AND COLUMN_NAME = @ColumnName";
                
                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@TableName", _databaseSettings.TableName);
                command.Parameters.AddWithValue("@ColumnName", columnName);
                
                var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Marks records as processed/uploaded in the database using the Sent column
        /// </summary>
        /// <param name="recordIds">IDs of processed records</param>
        public async Task MarkRecordsAsProcessedAsync(IEnumerable<string> recordIds)
        {
            try
            {
                if (!recordIds.Any())
                {
                    _logger.LogDebug("No records to mark as processed");
                    return;
                }

                _logger.LogInformation("Marking {RecordCount} records as sent", recordIds.Count());

                using var connection = new SqlConnection(_databaseSettings.GetConnectionString());
                await connection.OpenAsync();

                // Check if ImageSent column exists for additional tracking
                var hasImageSentColumn = await CheckColumnExistsAsync(connection, "ImageSent");

                // Update records to mark as sent
                string updateQuery;
                if (hasImageSentColumn)
                {
                    updateQuery = $@"
                        UPDATE [{_databaseSettings.TableName}] 
                        SET Sent = 1,
                            ImageSent = 1
                        WHERE Id IN ({string.Join(",", recordIds.Select((_, i) => $"@Id{i}"))})";
                }
                else
                {
                    updateQuery = $@"
                        UPDATE [{_databaseSettings.TableName}] 
                        SET Sent = 1
                        WHERE Id IN ({string.Join(",", recordIds.Select((_, i) => $"@Id{i}"))})";
                }

                using var command = new SqlCommand(updateQuery, connection);

                // Add parameters for each record ID
                for (int i = 0; i < recordIds.Count(); i++)
                {
                    command.Parameters.AddWithValue($"@Id{i}", recordIds.ElementAt(i));
                }

                var affectedRows = await command.ExecuteNonQueryAsync();
                _logger.LogInformation("Successfully marked {AffectedRows} records as sent", affectedRows);

                if (affectedRows != recordIds.Count())
                {
                    _logger.LogWarning("Expected to update {Expected} records but updated {Actual}", 
                        recordIds.Count(), affectedRows);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking records as processed");
                throw; // Re-throw so caller knows about the failure
            }
        }

        /// <summary>
        /// Gets database statistics for monitoring
        /// </summary>
        /// <returns>Database statistics</returns>
        public async Task<(int TotalRecords, int UnsentRecords, int SentRecords, DateTime? OldestUnsent, DateTime? NewestRecord)> GetDatabaseStatisticsAsync()
        {
            try
            {
                using var connection = new SqlConnection(_databaseSettings.GetConnectionString());
                await connection.OpenAsync();

                var hasSentColumn = await CheckColumnExistsAsync(connection, "Sent");
                
                // Get total records
                var totalCommand = new SqlCommand($"SELECT COUNT(*) FROM [{_databaseSettings.TableName}]", connection);
                var totalRecords = Convert.ToInt32(await totalCommand.ExecuteScalarAsync());

                int unsentRecords = 0;
                int sentRecords = 0;
                DateTime? oldestUnsent = null;
                
                if (hasSentColumn)
                {
                    // Get unsent records count
                    var unsentCommand = new SqlCommand($"SELECT COUNT(*) FROM [{_databaseSettings.TableName}] WHERE Sent = 0", connection);
                    unsentRecords = Convert.ToInt32(await unsentCommand.ExecuteScalarAsync());
                    
                    // Get sent records count
                    var sentCommand = new SqlCommand($"SELECT COUNT(*) FROM [{_databaseSettings.TableName}] WHERE Sent = 1", connection);
                    sentRecords = Convert.ToInt32(await sentCommand.ExecuteScalarAsync());
                    
                    // Get oldest unsent record date
                    if (unsentRecords > 0)
                    {
                        var oldestUnsentCommand = new SqlCommand($"SELECT MIN(ItemDateTime) FROM [{_databaseSettings.TableName}] WHERE Sent = 0", connection);
                        var result = await oldestUnsentCommand.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            oldestUnsent = Convert.ToDateTime(result);
                        }
                    }
                }
                else
                {
                    // If no Sent column, all records are considered unsent
                    unsentRecords = totalRecords;
                    sentRecords = 0;
                }
                
                // Get newest record date
                DateTime? newestRecord = null;
                var newestCommand = new SqlCommand($"SELECT MAX(ItemDateTime) FROM [{_databaseSettings.TableName}]", connection);
                var newestResult = await newestCommand.ExecuteScalarAsync();
                if (newestResult != null && newestResult != DBNull.Value)
                {
                    newestRecord = Convert.ToDateTime(newestResult);
                }

                return (totalRecords, unsentRecords, sentRecords, oldestUnsent, newestRecord);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting database statistics");
                return (0, 0, 0, null, null);
            }
        }

        /// <summary>
        /// Retrieves the total record count from the table
        /// </summary>
        private async Task<int> GetRecordCountAsync(SqlConnection connection)
        {
            try
            {
                var countCommand = new SqlCommand($"SELECT COUNT(*) FROM [{_databaseSettings.TableName}]", connection);
                return Convert.ToInt32(await countCommand.ExecuteScalarAsync());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting record count");
                return 0;
            }
        }

        /// <summary>
        /// Retrieves the unsent record count from the table
        /// </summary>
        private async Task<int> GetUnsentRecordCountAsync(SqlConnection connection)
        {
            try
            {
                var unsentCountCommand = new SqlCommand($"SELECT COUNT(*) FROM [{_databaseSettings.TableName}] WHERE Sent = 0", connection);
                return Convert.ToInt32(await unsentCountCommand.ExecuteScalarAsync());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting unsent record count");
                return 0;
            }
        }

        /// <summary>
        /// Retrieves the date of the oldest unsent record
        /// </summary>
        private async Task<DateTime?> GetOldestUnsentRecordDateAsync(SqlConnection connection)
        {
            try
            {
                var oldestUnsentCommand = new SqlCommand($"SELECT TOP 1 ItemDateTime FROM [{_databaseSettings.TableName}] WHERE Sent = 0 ORDER BY ItemDateTime ASC", connection);
                var result = await oldestUnsentCommand.ExecuteScalarAsync();
                return result != null && result != DBNull.Value ? (DateTime?)Convert.ToDateTime(result) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting oldest unsent record date");
                return null;
            }
        }

        /// <summary>
        /// Retrieves the date of the newest record in the table
        /// </summary>
        private async Task<DateTime?> GetNewestRecordDateAsync(SqlConnection connection)
        {
            try
            {
                var newestRecordCommand = new SqlCommand($"SELECT TOP 1 ItemDateTime FROM [{_databaseSettings.TableName}] ORDER BY ItemDateTime DESC", connection);
                var result = await newestRecordCommand.ExecuteScalarAsync();
                return result != null && result != DBNull.Value ? (DateTime?)Convert.ToDateTime(result) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting newest record date");
                return null;
            }
        }

        /// <summary>
        /// Safely gets a decimal value from the data reader
        /// </summary>
        private static decimal GetSafeDecimal(SqlDataReader reader, string columnName)
        {
            try
            {
                var value = reader[columnName];
                if (value == null || value == DBNull.Value)
                    return 0m;

                return Convert.ToDecimal(value);
            }
            catch
            {
                return 0m;
            }
        }

        /// <summary>
        /// Safely gets a string value from the data reader
        /// </summary>
        private static string GetSafeString(SqlDataReader reader, string columnName)
        {
            try
            {
                var value = reader[columnName];
                if (value == null || value == DBNull.Value)
                    return string.Empty;

                return value.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Safely gets a DateTime value from the data reader
        /// </summary>
        private static DateTime GetSafeDateTime(SqlDataReader reader, string columnName)
        {
            try
            {
                var value = reader[columnName];
                if (value == null || value == DBNull.Value)
                    return DateTime.MinValue;

                return Convert.ToDateTime(value);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        /// <summary>
        /// Safely gets a bit/boolean value from the data reader
        /// </summary>
        private static bool GetSafeBit(SqlDataReader reader, string columnName)
        {
            try
            {
                var value = reader[columnName];
                if (value == null || value == DBNull.Value)
                    return false;

                return Convert.ToBoolean(value);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Safely gets an integer value from the data reader
        /// </summary>
        private static int GetSafeInt(SqlDataReader reader, string columnName)
        {
            try
            {
                var value = reader[columnName];
                if (value == null || value == DBNull.Value)
                    return 0;

                return Convert.ToInt32(value);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Calculates Weight_Ratio using the formula: ((L*W*H)/10000) - (weight/1000)
        /// This represents the difference between theoretical volume-based weight and actual measured weight
        /// </summary>
        /// <param name="length">Length in millimeters</param>
        /// <param name="width">Width in millimeters</param>
        /// <param name="height">Height in millimeters</param>
        /// <param name="weight">Weight in grams</param>
        /// <returns>Calculated weight ratio</returns>
        public static decimal CalculateWeightRatio(decimal length, decimal width, decimal height, decimal weight)
        {
            // Formula from specifications: ((L*W*H)/10000) - (weight/1000)
            var volumeBasedWeight = (length * width * height) / 10000m;
            var actualWeightInKg = weight / 1000m;
            return volumeBasedWeight - actualWeightInKg;
        }

        /// <summary>
        /// Demonstrates the Weight_Ratio calculation with example values
        /// </summary>
        /// <returns>Demonstration results as a formatted string</returns>
        public string DemonstrateWeightRatioCalculation()
        {
            var demo = new System.Text.StringBuilder();
            demo.AppendLine("=== Weight_Ratio Calculation Demonstration ===");
            demo.AppendLine("Formula: Weight_Ratio = ((L*W*H)/10000) - (weight/1000)");
            demo.AppendLine("Where:");
            demo.AppendLine("  L = Length in mm");
            demo.AppendLine("  W = Width in mm");
            demo.AppendLine("  H = Height in mm");
            demo.AppendLine("  weight = Weight in grams");
            demo.AppendLine();
            
            // Example calculations
            var examples = new[]
            {
                new { L = 100m, W = 50m, H = 25m, Weight = 200m, Description = "Small item" },
                new { L = 250m, W = 150m, H = 100m, Weight = 1500m, Description = "Medium item" },
                new { L = 300m, W = 200m, H = 150m, Weight = 3000m, Description = "Large item" }
            };
            
            foreach (var example in examples)
            {
                var volumeWeight = (example.L * example.W * example.H) / 10000m;
                var actualWeight = example.Weight / 1000m;
                var weightRatio = CalculateWeightRatio(example.L, example.W, example.H, example.Weight);
                
                demo.AppendLine($"{example.Description}:");
                demo.AppendLine($"  Dimensions: {example.L} x {example.W} x {example.H} mm");
                demo.AppendLine($"  Actual weight: {example.Weight} g");
                demo.AppendLine($"  Volume-based weight: ({example.L}*{example.W}*{example.H})/10000 = {volumeWeight:F4}");
                demo.AppendLine($"  Weight in kg: {example.Weight}/1000 = {actualWeight:F3}");
                demo.AppendLine($"  Weight_Ratio: {volumeWeight:F4} - {actualWeight:F3} = {weightRatio:F4}");
                demo.AppendLine();
            }
            
            demo.AppendLine("Interpretation:");
            demo.AppendLine("  Positive Weight_Ratio: Item is lighter than expected based on volume");
            demo.AppendLine("  Negative Weight_Ratio: Item is heavier than expected based on volume");
            demo.AppendLine("  Near-zero Weight_Ratio: Item weight matches volume-based expectation");
            
            var result = demo.ToString();
            _logger.LogInformation(result);
            return result;
        }
    }

    /// <summary>
    /// Sample implementation for testing/demonstration purposes
    /// </summary>
    public class SampleDataRetrievalService : IDataRetrievalService
    {
        private readonly ILogger<SampleDataRetrievalService> _logger;
        private readonly DatabaseSettings? _databaseSettings;

        public SampleDataRetrievalService(ILogger<SampleDataRetrievalService> logger, AppSettings settings)
        {
            _logger = logger;
            _databaseSettings = settings.Database;
        }

        public async Task<bool> TestConnectionAsync()
        {
            _logger.LogInformation("Sample service - connection test always passes");
            await Task.Delay(10);
            return true;
        }

        public async Task<IEnumerable<(Dictionary<string, object> Data, string RecordId)>> GetDataForUploadAsync()
        {
            try
            {
                _logger.LogInformation("Retrieving sample data for demonstration...");
                
                await Task.Delay(100);
                
                var sampleData = new List<(Dictionary<string, object> Data, string RecordId)>();
                
                // Create a few sample records with calculated Weight_Ratio
                for (int i = 1; i <= 3; i++)
                {
                    var recordId = $"SAMPLE_{DateTime.Now:yyyyMMddHHmmss}_{i}";
                    
                    // Sample dimensions and weight
                    var length = 250m + (i * 10);  // Sample lengths: 260, 270, 280
                    var width = 2.5m + (i * 0.1m);  // Sample widths: 2.6, 2.7, 2.8
                    var height = 150m + (i * 5);    // Sample heights: 155, 160, 165
                    var rawWeight = 500m + (i * 50); // Sample weights: 550, 600, 650
                    
                    // Calculate Weight_Ratio using the formula: ((L*W*H)/10000) - (weight/1000)
                    var volumeBasedWeight = (length * width * height) / 10000m;
                    var actualWeightInKg = rawWeight / 1000m;
                    var weightRatio = volumeBasedWeight - actualWeightInKg;
                    
                    var data = new Dictionary<string, object>
                    {
                        {"Weight_Ratio", weightRatio},
                        {"LiquidVolume", 100.0m + (i * 10.0m)},
                        {"Barcode", $"PRD{DateTime.Now:yyyyMMdd}{i:D3}"},
                        {"Length", length},
                        {"Width", width},
                        {"Height", height},
                        {"RecordId", recordId},
                        {"RawWeight", rawWeight}
                    };
                    
                    _logger.LogDebug("Sample record {RecordId}: L={Length}, W={Width}, H={Height}, Weight={Weight}, Calculated Weight_Ratio={WeightRatio}", 
                        recordId, length, width, height, rawWeight, weightRatio);
                    
                    sampleData.Add((data, recordId));
                }
                
                _logger.LogInformation("Retrieved {RecordCount} sample records with calculated Weight_Ratio", sampleData.Count);
                return sampleData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving sample data");
                return new List<(Dictionary<string, object> Data, string RecordId)>();
            }
        }

        public async Task MarkRecordsAsProcessedAsync(IEnumerable<string> recordIds)
        {
            try
            {
                _logger.LogInformation("Sample service - marking {RecordCount} records as processed", recordIds.Count());
                await Task.Delay(50);
                
                foreach (var id in recordIds)
                {
                    _logger.LogDebug("Sample service - marked as processed: {RecordId}", id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in sample mark processed");
                throw;
            }
        }

        /// <summary>
        /// Gets database statistics for monitoring
        /// </summary>
        /// <returns>Database statistics</returns>
        public async Task<(int TotalRecords, int UnsentRecords, int SentRecords, DateTime? OldestUnsent, DateTime? NewestRecord)> GetDatabaseStatisticsAsync()
        {
            _logger.LogInformation("Sample service - returning mock statistics");
            await Task.Delay(10);
            return (100, 25, 75, DateTime.Now.AddDays(-7), DateTime.Now);
        }
    }
}