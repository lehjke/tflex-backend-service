using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TFlexAutomationRunner
{
    internal static class TFlexApiBootstrap
    {
        private static readonly List<string> AssemblyFolders = new List<string>();

        public static void Initialize()
        {
            if (AssemblyFolders.Count > 0)
            {
                return;
            }

            var programDirectory = FindProgramDirectory();
            AssemblyFolders.Add(programDirectory);

            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;

            SetDllDirectory(programDirectory);
            Environment.SetEnvironmentVariable(
                "PATH",
                programDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));

            Directory.SetCurrentDirectory(programDirectory);
        }

        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            var assemblyName = new AssemblyName(args.Name).Name;

            foreach (var folder in AssemblyFolders)
            {
                var path = Path.Combine(folder, assemblyName + ".dll");
                if (File.Exists(path))
                {
                    return Assembly.LoadFile(path);
                }
            }

            return null;
        }

        private static string FindProgramDirectory()
        {
            var environmentPath = Environment.GetEnvironmentVariable("TFLEX_CAD_PROGRAM_DIR");
            if (IsProgramDirectory(environmentPath))
            {
                return Path.GetFullPath(environmentPath);
            }

            foreach (var registryPath in new[]
            {
                @"SOFTWARE\Top Systems\T-FLEX CAD 3D 17\Rus",
                @"SOFTWARE\Top Systems\T-FLEX CAD 17\Rus"
            })
            {
                var path = ReadRegistryProgramFolder(registryPath);
                if (IsProgramDirectory(path))
                {
                    return Path.GetFullPath(path);
                }
            }

            var defaultPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "T-FLEX CAD 17",
                "Program");

            if (IsProgramDirectory(defaultPath))
            {
                return defaultPath;
            }

            throw new FileNotFoundException(
                "T-FLEX CAD 17 Open API was not found. Set TFLEX_CAD_PROGRAM_DIR to the folder containing TFlexAPI.dll.");
        }

        private static string ReadRegistryProgramFolder(string registryPath)
        {
            using (var key = Registry.LocalMachine.OpenSubKey(registryPath, false))
            {
                if (key == null)
                {
                    return string.Empty;
                }

                var programFolder = Convert.ToString(key.GetValue("ProgramFolder"));
                if (!string.IsNullOrWhiteSpace(programFolder))
                {
                    return programFolder;
                }

                var setupHelpPath = Convert.ToString(key.GetValue("SetupHelpPath"));
                if (!string.IsNullOrWhiteSpace(setupHelpPath))
                {
                    return setupHelpPath;
                }
            }

            return string.Empty;
        }

        private static bool IsProgramDirectory(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && File.Exists(Path.Combine(path, "TFlexAPI.dll"))
                && File.Exists(Path.Combine(path, "TFlexCad.exe"));
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);
    }
}
