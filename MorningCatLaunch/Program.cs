using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;

namespace MorningCatLaunch
{
    class Program
    {
        private static readonly string _rootDir = AppContext.BaseDirectory;
        private static readonly string _launchCorePath = Path.Combine(_rootDir, "MorningCatLaunchCore.dll");

        static int Main(string[] args)
        {
            Console.WriteLine("[ML] Starting...");

            var startArgsPath = Path.Combine(_rootDir, "start.args");
            if (File.Exists(startArgsPath))
            {
                var content = File.ReadAllText(startArgsPath).Trim();
                if (!string.IsNullOrEmpty(content))
                {
                    args = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    Console.WriteLine($"[ML] Loaded start.args: {string.Join(" ", args)}");
                }
            }

            if (!EnsureLaunchCore())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ML] Failed to ensure LaunchCore");
                Console.ResetColor();
                Thread.Sleep(5000);
                return 1;
            }

            while (true)
            {
                int exitCode = RunLaunchCore(args);

                if (exitCode == 100)
                {
                    Console.WriteLine("[ML] Restart requested, reloading LaunchCore...");
                    ForceGc();
                    Thread.Sleep(500);
                    continue;
                }

                return exitCode;
            }
        }

        private static bool EnsureLaunchCore()
        {
            if (File.Exists(_launchCorePath))
            {
                Console.WriteLine("[ML] MorningCatLaunchCore.dll found");
                return true;
            }

            Console.WriteLine("[ML] MorningCatLaunchCore.dll not found, extracting from embedded resource...");
            return ExtractEmbeddedLaunchCore();
        }

        private static bool ExtractEmbeddedLaunchCore()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("mlc.zip", StringComparison.OrdinalIgnoreCase));

                if (resourceName == null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[ML] Embedded resource mlc.zip not found");
                    Console.ResetColor();
                    return false;
                }

                using var resourceStream = assembly.GetManifestResourceStream(resourceName);
                if (resourceStream == null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[ML] Failed to read embedded resource");
                    Console.ResetColor();
                    return false;
                }

                Console.WriteLine("[ML] Extracting LaunchCore...");

                using var archive = new ZipArchive(resourceStream, ZipArchiveMode.Read);
                foreach (var entry in archive.Entries)
                {
                    var entryName = entry.FullName;

                    if (entryName.EndsWith("/") || entryName.EndsWith("\\"))
                        continue;

                    var destPath = Path.Combine(_rootDir, entryName);
                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);

                    entry.ExtractToFile(destPath, true);
                    Console.WriteLine($"[ML] Extracted: {entryName}");
                }

                Console.WriteLine("[ML] Extraction complete");
                return File.Exists(_launchCorePath);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ML] Extraction failed: {ex.Message}");
                Console.ResetColor();
                return false;
            }
        }

        private static int RunLaunchCore(string[] args)
        {
            Console.WriteLine("[ML] Loading MorningCatLaunchCore from stream...");

            var loadContext = new AssemblyLoadContext("LaunchCoreContext", true);
            var dllBytesCache = new ConcurrentDictionary<string, byte[]>();

            try
            {
                var coreBytes = File.ReadAllBytes(_launchCorePath);
                using (var stream = new MemoryStream(coreBytes))
                {
                    loadContext.LoadFromStream(stream);
                }

                var mainType = loadContext.Assemblies
                    .FirstOrDefault(a => a.GetName().Name == "MorningCatLaunchCore")
                    ?.GetType("MorningCatLaunchCore.LaunchCore");

                if (mainType == null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[ML] Could not find LaunchCore type in MorningCatLaunchCore");
                    Console.ResetColor();
                    Thread.Sleep(5000);
                    return 1;
                }

                var runMethod = mainType.GetMethod("Run", new[] { typeof(string[]) });
                if (runMethod == null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[ML] Could not find Run(string[]) method in LaunchCore");
                    Console.ResetColor();
                    Thread.Sleep(5000);
                    return 1;
                }

                var result = runMethod.Invoke(null, new object[] { args });

                if (result is int exitCode)
                    return exitCode;

                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ML] LaunchCore failed: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[ML] Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                    Console.WriteLine($"[ML] Inner stack:\n{ex.InnerException.StackTrace}");
                }
                Console.WriteLine($"[ML] Stack:\n{ex.StackTrace}");
                Console.ResetColor();
                Thread.Sleep(10000);
                return 1;
            }
            finally
            {
                dllBytesCache.Clear();
                try { loadContext.Unload(); } catch { }
                ForceGc();
            }
        }

        private static void ForceGc()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
