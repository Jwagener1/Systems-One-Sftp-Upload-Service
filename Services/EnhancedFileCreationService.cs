using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Systems_One_Sftp_Upload_Service.Models;

namespace Systems_One_Sftp_Upload_Service.Services
{
    /// <summary>
    /// Enhanced version of FileCreationService with improved file archiving functionality
    /// </summary>
    public class EnhancedFileCreationService : FileCreationService
    {
        private readonly ILogger<EnhancedFileCreationService> _logger;

        public EnhancedFileCreationService(
            ILogger<EnhancedFileCreationService> logger,
            AppSettings settings,
            MessageFormatter messageFormatter)
            : base(logger, settings, messageFormatter)
        {
            _logger = logger;
        }

        /// <summary>
        /// Archives a file after successful upload by copying it to the archive directory and then deleting the original
        /// This enhanced version fixes the "file already exists" error that can occur with File.Move
        /// </summary>
        /// <param name="filePath">Path to the file to archive</param>
        /// <param name="uploadTimestamp">Optional timestamp when the file was uploaded</param>
        /// <returns>The path where the file was archived</returns>
        public override async Task<string> ArchiveFileAsync(string filePath, DateTime? uploadTimestamp = null)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"File not found: {filePath}");
                }

                var archiveDir = EnsureArchiveDirectoryExists();
                var fileName = Path.GetFileName(filePath);
                var timestamp = uploadTimestamp ?? DateTime.Now;
                
                // Create a subdirectory for the date to organize archived files
                var dateFolderName = timestamp.ToString("yyyy-MM-dd");
                var dateArchiveDir = Path.Combine(archiveDir, dateFolderName);
                
                if (!Directory.Exists(dateArchiveDir))
                {
                    Directory.CreateDirectory(dateArchiveDir);
                    _logger.LogDebug("Created date archive directory: {DateArchiveDir}", dateArchiveDir);
                }
                
                // Generate archived filename with upload timestamp
                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                var fileExt = Path.GetExtension(fileName);
                var archivedFileName = $"{fileNameWithoutExt}_uploaded_{timestamp:HHmmss}{fileExt}";
                var archivedFilePath = Path.Combine(dateArchiveDir, archivedFileName);
                
                // Handle duplicate filenames by adding a counter if the file already exists
                int counter = 1;
                while (File.Exists(archivedFilePath))
                {
                    archivedFileName = $"{fileNameWithoutExt}_uploaded_{timestamp:HHmmss}_{counter}{fileExt}";
                    archivedFilePath = Path.Combine(dateArchiveDir, archivedFileName);
                    counter++;
                    
                    // If we've tried too many times with simple counter, add a unique component
                    if (counter > 10)
                    {
                        archivedFileName = $"{fileNameWithoutExt}_uploaded_{timestamp:HHmmss}_{Guid.NewGuid().ToString().Substring(0, 8)}{fileExt}";
                        archivedFilePath = Path.Combine(dateArchiveDir, archivedFileName);
                        break;
                    }
                }
                
                try
                {
                    // Use Copy + Delete instead of Move to avoid "file already exists" errors
                    // This is more reliable when archiving files
                    File.Copy(filePath, archivedFilePath);
                    File.Delete(filePath);
                }
                catch (IOException ex) when (ex.Message.Contains("already exists"))
                {
                    // If the destination file was created by another process between our check and copy,
                    // generate a unique name with a GUID and try again
                    var uniqueId = Guid.NewGuid().ToString().Substring(0, 8);
                    archivedFileName = $"{fileNameWithoutExt}_uploaded_{timestamp:HHmmss}_{uniqueId}{fileExt}";
                    archivedFilePath = Path.Combine(dateArchiveDir, archivedFileName);
                    
                    // Try again with the new unique filename
                    File.Copy(filePath, archivedFilePath);
                    File.Delete(filePath);
                }
                
                _logger.LogInformation("Archived file: {OriginalFile} ? {ArchivedFile}", fileName, archivedFileName);
                
                return archivedFilePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to archive file: {FilePath}", filePath);
                throw;
            }
        }
    }
}