#if !WSL
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using AutoMk.Models;

namespace AutoMk;



public class DriveWatcher
{
    private readonly RipSettings _settings;


    public DriveWatcher(RipSettings settings)
    {
        _settings = settings;
    }

    [DllImport("winmm.dll")]
    public static extern long mciSendString(string strCommand, StringBuilder? strReturn, int iReturnLength, IntPtr hwndCallback);
    public string OpenDrive(string letter)
    {
        string returnString = "";
        do
        {

            _ = mciSendString($"open {letter} type cdaudio alias cdname", null, 0, IntPtr.Zero);
            _ = mciSendString($"set cdname door open", null, 0, IntPtr.Zero);
            _ = mciSendString($"close cdname", null, 0, IntPtr.Zero);

            Thread.Sleep(1000);

        } while (IsDriveReady(letter));

        return returnString;
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
        Console.WriteLine("Drive List");
        var drives = GetDrives.ToList();
        foreach (
            DriveInfo drive in
            GetDrives)
        {
            Console.WriteLine("{0} type:{1} ready:{2}", drive.Name, drive.DriveType, drive.IsReady);
        }
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
    private IEnumerable<DriveInfo> GetDrives => DriveInfo.GetDrives().Where(driveInfo => driveInfo.DriveType.Equals(DriveType.CDRom) && !_settings.IgnoreDrives.Any(x => x.StartsWith(driveInfo.Name.Substring(0, 1), StringComparison.InvariantCultureIgnoreCase)));

}
#endif
