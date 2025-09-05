using AutoMk.Models;

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
            do
            {
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
                    Console.WriteLine("CD/DVD drive tray opened successfully.");
                }
                else
                {
                    //open drive
                    Thread.Sleep(1000);
                    Console.WriteLine("Failed to open CD/DVD drive tray.");
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
            Console.WriteLine("Drive List");
            List<DriveInfo> drives = GetDrives.ToList();
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
