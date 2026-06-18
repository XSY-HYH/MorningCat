using System;
using Logging;
using MorningCat.Config;
using MorningCat.GUI;
using MorningCat.I18n;

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
                Log.Info(I18nManager.S("webui.gui_disabled"));
                return;
            }

            try
            {
                _guiManager = new GuiManager();
                _guiManager.Initialize();

                _guiManager.SetWebuiPort(config.WebUI.Port);

                _guiManager.SetRestartCallback(() =>
                {
                    Log.Info(I18nManager.S("webui.gui_restart_requested"));
                    _ = RestartAsync();
                });

                _guiManager.SetShutdownCallback(() =>
                {
                    Log.Info(I18nManager.S("webui.gui_shutdown_requested"));
                    RequestExit();
                });

                _guiManager.Show();
                Log.Name("GUI");
                Log.Info(I18nManager.S("gui.started"));
            }
            catch (Exception ex)
            {
                Log.Warning(I18nManager.S("gui.start_failed", ex.Message));
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
                    Log.Info(I18nManager.S("gui.stopped"));
                }
                catch (Exception ex)
                {
                    Log.Debug(I18nManager.S("gui.stop_error", ex.Message));
                }
                finally
                {
                    _guiManager = null;
                }
            }
        }
    }
}
