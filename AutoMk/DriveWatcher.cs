#if !WSL
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Management;
using Microsoft.Win32.SafeHandles;
using AutoMk.Models;
using Spectre.Console;

namespace AutoMk;



public class DriveWatcher : IDisposable
{
    private readonly RipSettings _settings;
    private ManagementEventWatcher? _driveWatcher;
    private bool _disposed = false;

    // Modern Windows APIs for drive control
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    // Constants for drive operations
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint IOCTL_STORAGE_EJECT_MEDIA = 0x2D4808;
    private const uint IOCTL_STORAGE_LOAD_MEDIA = 0x2D480C;

    public DriveWatcher(RipSettings settings)
    {
        _settings = settings;
        InitializeEventWatcher();
    }

    private void InitializeEventWatcher()
    {
        try
        {
            // Watch for volume change events (disc insertion/removal)
            var query = new WqlEventQuery("SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 2");
            _driveWatcher = new ManagementEventWatcher(query);
            _driveWatcher.EventArrived += OnDriveChanged;
            _driveWatcher.Start();
        }
        catch (Exception ex)
        {
            // Fall back to polling if WMI fails
            AnsiConsole.MarkupLine($"[yellow]Warning:[/] Could not initialize WMI drive watcher: {Markup.Escape(ex.Message)}");
        }
    }

    private void OnDriveChanged(object sender, EventArrivedEventArgs e)
    {
        try
        {
            // This event fires when a disc is inserted
            // The actual processing will still be handled by the main loop
            // but this reduces the need for constant polling
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error in drive change event:[/] {Markup.Escape(ex.Message)}");
        }
    }

    public string OpenDrive(string letter)
    {
        const int maxAttempts = 3; // Reduced attempts since modern API is more reliable
        
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                // Format drive path for CreateFile (e.g., \\.\D:)
                string devicePath = $"\\\\.\\{letter.TrimEnd(':')}:";
                
                // Open handle to the drive
                SafeFileHandle driveHandle = CreateFile(
                    devicePath,
                    GENERIC_READ,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (driveHandle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (attempt == maxAttempts)
                    {
                        return $"Warning: Could not open drive {letter} (Error: {error})";
                    }
                    Thread.Sleep(1000);
                    continue;
                }

                // Send eject command
                bool success = DeviceIoControl(
                    driveHandle,
                    IOCTL_STORAGE_EJECT_MEDIA,
                    IntPtr.Zero, 0,
                    IntPtr.Zero, 0,
                    out uint bytesReturned,
                    IntPtr.Zero);

                driveHandle.Dispose();

                if (success)
                {
                    // Wait a moment and check if drive is no longer ready
                    Thread.Sleep(2000);
                    if (!IsDriveReady(letter))
                    {
                        return ""; // Success - no warning message
                    }
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    if (attempt == maxAttempts)
                    {
                        return $"Warning: Drive {letter} eject failed (Error: {error})";
                    }
                }
            }
            catch (Exception ex)
            {
                if (attempt == maxAttempts)
                {
                    return $"Warning: Exception during drive {letter} eject: {ex.Message}";
                }
            }

            Thread.Sleep(1000);
        }

        return $"Warning: Drive {letter} did not eject after {maxAttempts} attempts";
    }




    public bool IsDriveReady(string letter)
    {
        var ready = false;
        foreach (
            DriveInfo drive in GetDrives.Where(driveInfo => driveInfo.Name.StartsWith(letter, StringComparison.InvariantCultureIgnoreCase)))
        {
            ready = true;
            if (!drive.IsReady)
            {
                return false;
            }
        }

        return ready;
    }

    public List<DriveInfo> PrintDrives()
    {
        var drives = GetDrives.ToList();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .Title("[cyan]Drive List[/]")
            .AddColumn(new TableColumn("[white]Drive[/]").Centered())
            .AddColumn(new TableColumn("[white]Type[/]").Centered())
            .AddColumn(new TableColumn("[white]Status[/]").Centered());

        foreach (DriveInfo drive in drives)
        {
            var statusColor = drive.IsReady ? "green" : "dim";
            var statusText = drive.IsReady ? "Ready" : "Not Ready";
            table.AddRow(
                $"[white]{Markup.Escape(drive.Name)}[/]",
                $"[dim]{drive.DriveType}[/]",
                $"[{statusColor}]{statusText}[/]"
            );
        }

        AnsiConsole.Write(table);
        return drives;
    }

    public bool AnyDriveReady()
    {

        foreach (
            DriveInfo drive in GetDrives)
        {

            if (drive.IsReady)
            {
                return true;
            }
        }

        return false;
    }
    public bool AllDrivesReady()
    {
        var ready = false;
        foreach (
            DriveInfo drive in
            GetDrives)
        {
            ready = true;

            if (!drive.IsReady)
            {
                return false;
            }
        }

        return ready;
    }

    public bool CanEjectDrive(string letter)
    {
        try
        {
            string devicePath = $"\\\\.\\{letter.TrimEnd(':')}:";
            
            SafeFileHandle driveHandle = CreateFile(
                devicePath,
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (driveHandle.IsInvalid)
            {
                return false;
            }

            driveHandle.Dispose();
            return true; // If we can open it, we can likely eject it
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                try
                {
                    _driveWatcher?.Stop();
                    _driveWatcher?.Dispose();
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Error disposing drive watcher:[/] {Markup.Escape(ex.Message)}");
                }
            }
            _disposed = true;
        }
    }

    private IEnumerable<DriveInfo> GetDrives => DriveInfo.GetDrives().Where(driveInfo => driveInfo.DriveType.Equals(DriveType.CDRom) && !_settings.IgnoreDrives.Any(x => x.StartsWith(driveInfo.Name.Substring(0, 1), StringComparison.InvariantCultureIgnoreCase)));

}
#endif
