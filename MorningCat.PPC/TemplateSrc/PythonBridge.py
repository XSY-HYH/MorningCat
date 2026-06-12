import json

_morningcat = None
_plugin_instance = None

class PluginBase:
    def on_init(self):
        pass

    def on_exit(self):
        pass

    def on_message(self, event):
        pass

    def log(self, message):
        if _morningcat:
            _morningcat.LogInfo(str(message))

    def send_message(self, user_id, message):
        if _morningcat:
            _morningcat.SendMessage(int(user_id), str(message))

    def send_group_message(self, group_id, message):
        if _morningcat:
            _morningcat.SendGroupMessage(int(group_id), str(message))

def _init_bridge(morningcat_obj):
    global _morningcat
    _morningcat = morningcat_obj

def _set_plugin_instance(instance):
    global _plugin_instance
    _plugin_instance = instance

def __on_exit__():
    global _plugin_instance
    if _plugin_instance:
        try:
            _plugin_instance.on_exit()
        except Exception:
            pass
