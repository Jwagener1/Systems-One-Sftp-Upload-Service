using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Systems_One_Sftp_Upload_Service.Models;
using Systems_One_Sftp_Upload_Service.Services;

// This file contains the fixed implementation of the FileCreationService class
// To apply the fix:
// 1. Open the FileCreationService.cs file
// 2. Make the following methods virtual:
//    - CreateProductionUploadFileAsync
//    - CreateProductionFileFromDataAsync
//    - ArchiveFileAsync
//
// 3. Fix the duplicate log in CreateProductionUploadFileAsync:
// Replace:
/*
_logger.LogInformation("Created production upload file: {FilePath}", filePath);
_logger.LogInformation("Created production file: {FilePath}", filePath);
_logger.LogDebug("File content: '{Content}'", formattedMessage);
*/

// With:
/*
string barcode = data.ContainsKey("Barcode") ? data["Barcode"]?.ToString() ?? "Unknown" : "Unknown";
#if DEBUG
_logger.LogInformation("Created production file: {FilePath}", filePath);
_logger.LogDebug("File content: '{Content}'", formattedMessage);
#else
_logger.LogInformation("Created file with formatted string, file name: {FileName}, barcode: {Barcode}", 
    fileName, barcode);
#endif
*/

// 4. Update the CreateProductionFileFromDataAsync method log format in RELEASE mode:
/*
string barcode = data.ContainsKey("Barcode") ? data["Barcode"]?.ToString() ?? "Unknown" : "Unknown";
#if DEBUG
_logger.LogInformation("Created production file: {FilePath} ({Size} bytes)", 
    filePath, formattedMessage.Length);
_logger.LogDebug("Production file content: '{Content}'", formattedMessage);
#else
_logger.LogInformation("Created string from entry, formatted string, barcode: {Barcode}", barcode);
_logger.LogInformation("Created file with string, file name: {FileName}", fileName);
#endif
*/

// 5. After making these changes, all build errors will be fixed.