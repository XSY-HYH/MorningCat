using System;
using Logging;
using MorningCat.Config;
using MorningCat.GUI;

namespace MorningCat
{
    public partial class MorningCatBot
    {
        private GuiManager? _guiManager;

        public GuiManager? Gui => _guiManager;

        public void StartGui()
        {
            var config = _configManager.GetConfig();

            if (!config.EnableGui)
            {
                Log.Name("GUI");
                Log.Info("GUI已禁用");
                return;
            }

            try
            {
                _guiManager = new GuiManager();
                _guiManager.Initialize();

                _guiManager.SetWebuiPort(config.WebUI.Port);

                _guiManager.SetRestartCallback(() =>
                {
                    Log.Info("GUI请求重启...");
                    _ = RestartAsync();
                });

                _guiManager.SetShutdownCallback(() =>
                {
                    Log.Info("GUI请求关闭...");
                    RequestExit();
                });

                _guiManager.Show();
                Log.Name("GUI");
                Log.Info("GUI已启动");
            }
            catch (Exception ex)
            {
                Log.Warning($"GUI启动失败: {ex.Message}");
                _guiManager = null;
            }
        }

        public void StopGui()
        {
            if (_guiManager != null)
            {
                try
                {
                    _guiManager.Shutdown();
                    Log.Name("GUI");
                    Log.Info("GUI已关闭");
                }
                catch (Exception ex)
                {
                    Log.Debug($"GUI关闭异常: {ex.Message}");
                }
                finally
                {
                    _guiManager = null;
                }
            }
        }
    }
}
