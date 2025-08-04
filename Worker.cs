using Serilog;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Systems_One_Sftp_Upload_Service.Models;

namespace Systems_One_Sftp_Upload_Service
{
    /// <summary>
    /// Worker service for handling SFTP uploads
    /// </summary>
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly AppSettings _settings;

        /// <summary>
        /// Initializes a new instance of the Worker class
        /// </summary>
        /// <param name="logger">Logger instance</param>
        /// <param name="settings">Application settings</param>
        public Worker(ILogger<Worker> logger, AppSettings settings)
        {
            _logger = logger;
            _settings = settings;
        }

        /// <summary>
        /// Main worker process that runs continuously
        /// </summary>
        /// <param name="stoppingToken">Token for cancellation notification</param>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                    
                    // Log information about the settings
                    if (_settings.General != null)
                    {
                        _logger.LogInformation("Upload Interval: {Interval} ms", _settings.General.GetUploadIntervalMs());
                    }
                    
                    if (_settings.Sftp != null)
                    {
                        _logger.LogInformation("SFTP Host: {Host}:{Port}", _settings.Sftp.Host, _settings.Sftp.GetPort());
                        _logger.LogInformation("SFTP Remote Directory: {RemoteDir}", _settings.Sftp.RemoteDirectory);
                    }
                }
                
                // Use the polling interval from settings
                int delay = _settings.General?.GetUploadIntervalMs() ?? 500;
                await Task.Delay(delay, stoppingToken);
            }
        }
    }
}
