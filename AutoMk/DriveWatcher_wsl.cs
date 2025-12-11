using AutoMk.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Spectre.Console;

namespace AutoMk
{
#if WSL

    public class DriveWatcher
    {
        private readonly RipSettings _settings;


        public DriveWatcher(RipSettings settings)
        {
            _settings = settings;
        }

        //[DllImport("libcdrom.so")]
        //public static extern int isCdromOpen(string devicePath);
        //[DllImport("libcdrom.so")]
        //public static extern void listCdroms(out IntPtr driveNames, out int driveCount);

        public string OpenDrive(string letter)
        {
            string returnString = "";
            const int maxAttempts = 10; // Maximum 10 seconds to try ejecting
            int attempts = 0;
            
            do
            {
                attempts++;
                
                Process ejectProcess = new();
                ProcessStartInfo ejectStartInfo = new()
                {
                    FileName = "eject",
                    Arguments = letter,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                ejectProcess.StartInfo = ejectStartInfo;

                _ = ejectProcess.Start();
                ejectProcess.WaitForExit();

                if (ejectProcess.ExitCode == 0)
                {
                    AnsiConsole.MarkupLine("[green]CD/DVD drive tray opened successfully.[/]");
                }
                else
                {
                    //open drive
                    Thread.Sleep(1000);
                    AnsiConsole.MarkupLine("[yellow]Failed to open CD/DVD drive tray.[/]");
                }

                // Break if we've exceeded max attempts to prevent infinite loop
                if (attempts >= maxAttempts)
                {
                    returnString = $"Warning: Drive {letter} did not eject after {maxAttempts} attempts";
                    break;
                }

            } while (IsDriveReady(letter));

            return returnString;
        }




        public bool IsDriveReady(string letter)
        {
            bool ready = false;
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
            List<DriveInfo> drives = GetDrives.ToList();

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
            bool ready = false;
            IEnumerable<DriveInfo> items = GetDrives;
            foreach (
                DriveInfo drive in
                items)
            {
                ready = true;

                if (!drive.IsReady)
                {
                    return false;
                }
            }

            return ready;
        }
        private IEnumerable<DriveInfo> GetDrives => DriveInfo.GetDrives().Where(driveInfo => driveInfo.DriveType.Equals(DriveType.CDRom) && !_settings.IgnoreDrives.Any(x => x.StartsWith(driveInfo.Name[..1], StringComparison.InvariantCultureIgnoreCase)));

    }
#endif
}
