using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Systems_One_Sftp_Upload_Service.Services
{
    /// <summary>
    /// Helper class for file archiving operations that fixes the "file already exists" issue
    /// </summary>
    public static class FileArchivingFix
    {
        /// <summary>
        /// Archives a file safely by using copy and delete instead of move to avoid file conflicts
        /// </summary>
        /// <param name="logger">Logger for recording operations</param>
        /// <param name="filePath">Source file path</param>
        /// <param name="destinationPath">Destination file path</param>
        /// <returns>True if successful</returns>
        public static bool ArchiveFileSafely(ILogger logger, string filePath, string destinationPath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    logger.LogWarning("Source file not found: {FilePath}", filePath);
                    return false;
                }

                // If destination already exists, make it unique
                string finalDestination = destinationPath;
                string destDir = Path.GetDirectoryName(destinationPath);
                string fileName = Path.GetFileNameWithoutExtension(destinationPath);
                string fileExt = Path.GetExtension(destinationPath);
                
                int counter = 1;
                while (File.Exists(finalDestination))
                {
                    finalDestination = Path.Combine(destDir, $"{fileName}_{counter}{fileExt}");
                    counter++;
                    
                    // If we've tried too many times with simple counter, add a unique component
                    if (counter > 10)
                    {
                        var uniqueId = Guid.NewGuid().ToString().Substring(0, 8);
                        finalDestination = Path.Combine(destDir, $"{fileName}_{uniqueId}{fileExt}");
                        break;
                    }
                }
                
                // Use copy and delete instead of move
                File.Copy(filePath, finalDestination);
                File.Delete(filePath);
                
                logger.LogInformation("File archived successfully: {SourceFile} ? {DestinationFile}", 
                    Path.GetFileName(filePath), Path.GetFileName(finalDestination));
                
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to safely archive file: {FilePath}", filePath);
                return false;
            }
        }
    }
}