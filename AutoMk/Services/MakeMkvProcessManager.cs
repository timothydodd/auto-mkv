using System;
using System.Diagnostics;
using System.Threading.Tasks;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;

namespace AutoMk.Services;

public class MakeMkvProcessManager
{
    private readonly RipSettings _ripSettings;
    private readonly ILogger<MakeMkvProcessManager> _logger;

    public MakeMkvProcessManager(RipSettings ripSettings, ILogger<MakeMkvProcessManager> logger)
    {
        _ripSettings = ValidationHelper.ValidateNotNull(ripSettings);
        _logger = ValidationHelper.ValidateNotNull(logger);
    }

    public Process? CreateProcess(string arguments)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _ripSettings.MakeMKVPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            _logger.LogDebug("Created MakeMKV process with arguments: {Arguments}", arguments);
            return process;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create MakeMKV process");
            return null;
        }
    }

    public async Task<bool> ExecuteProcessAsync(Process process, Action<string>? outputHandler = null, Action<string>? errorHandler = null)
    {
        try
        {
            if (outputHandler != null)
            {
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        outputHandler(e.Data);
                };
            }

            if (errorHandler != null)
            {
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        errorHandler(e.Data);
                };
            }

            process.Start();

            if (outputHandler != null)
                process.BeginOutputReadLine();
            if (errorHandler != null)
                process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing MakeMKV process");
            return false;
        }
    }
}