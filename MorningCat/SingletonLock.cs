using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MorningCat
{
    public class SingletonLock : IDisposable
    {
        private readonly string _lockFilePath;
        private bool _disposed;

        public SingletonLock()
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _lockFilePath = Path.Combine(baseDirectory, ".lock");
        }

        public bool TryAcquire()
        {
            if (File.Exists(_lockFilePath))
            {
                return false;
            }

            try
            {
                File.WriteAllText(_lockFilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    File.SetAttributes(_lockFilePath, File.GetAttributes(_lockFilePath) | FileAttributes.Hidden);
                }
                
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Release()
        {
            try
            {
                if (File.Exists(_lockFilePath))
                {
                    File.Delete(_lockFilePath);
                }
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            Release();
            _disposed = true;
        }
    }
}
