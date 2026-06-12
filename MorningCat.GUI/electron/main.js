const { app, BrowserWindow, ipcMain } = require('electron');
const path = require('path');
const fs = require('fs');

app.disableHardwareAcceleration();
app.commandLine.appendSwitch('disable-gpu');
app.commandLine.appendSwitch('disable-software-rasterizer');
app.commandLine.appendSwitch('disable-extensions');
app.commandLine.appendSwitch('disable-plugins');
app.commandLine.appendSwitch('disable-sync');
app.commandLine.appendSwitch('disable-background-networking');
app.commandLine.appendSwitch('disable-default-apps');
app.commandLine.appendSwitch('disable-breakpad');
app.commandLine.appendSwitch('disable-component-update');
app.commandLine.appendSwitch('disable-client-side-phishing-detection');
app.commandLine.appendSwitch('disable-cookie-encryption');
app.commandLine.appendSwitch('disable-notifications');
app.commandLine.appendSwitch('disable-password-manager');
app.commandLine.appendSwitch('disable-domain-reliability');
app.commandLine.appendSwitch('disable-speech-api');
app.commandLine.appendSwitch('renderer-process-limit', '1');
app.commandLine.appendSwitch('js-flags', '--max-old-space-size=128');
app.commandLine.appendSwitch('disable-background-timer-throttling');
app.commandLine.appendSwitch('disable-renderer-backgrounding');

let win = null;
let webuiPort = 0;

for (let i = 1; i < process.argv.length; i++) {
    if (process.argv[i].startsWith('--webui-port=')) {
        webuiPort = parseInt(process.argv[i].split('=')[1]) || 0;
    }
}

function resolveWebUIPath() {
    const candidates = [
        path.join(__dirname, '..', 'MorningCat.WebUI', 'wwwroot', 'webui'),
        path.join(process.resourcesPath, 'webui'),
        path.join(__dirname, 'webui')
    ];

    for (const dir of candidates) {
        try {
            fs.accessSync(path.join(dir, 'index.html'));
            return dir;
        } catch {}
    }

    return candidates[0];
}

function createWindow() {
    win = new BrowserWindow({
        width: 1200,
        height: 800,
        minWidth: 900,
        minHeight: 600,
        title: 'MorningCat',
        backgroundColor: '#f0f8ff',
        webPreferences: {
            nodeIntegration: false,
            contextIsolation: true,
            sandbox: true,
            spellcheck: false,
            devTools: false,
            preload: path.join(__dirname, 'preload.js')
        },
        icon: path.join(__dirname, 'assets', 'icon.png'),
        show: false
    });

    if (webuiPort > 0) {
        win.loadURL(`http://127.0.0.1:${webuiPort}/webui/`);
    } else {
        const webuiDir = resolveWebUIPath();
        win.loadFile(path.join(webuiDir, 'index.html'));
    }

    win.once('ready-to-show', () => {
        win.show();
    });

    win.on('closed', () => {
        win = null;
    });
}

ipcMain.handle('window-minimize', () => {
    if (win) win.minimize();
});

ipcMain.handle('window-maximize', () => {
    if (win) {
        if (win.isMaximized()) {
            win.unmaximize();
        } else {
            win.maximize();
        }
    }
    return win ? win.isMaximized() : false;
});

ipcMain.handle('window-close', () => {
    if (win) win.close();
});

ipcMain.handle('window-is-maximized', () => {
    return win ? win.isMaximized() : false;
});

app.whenReady().then(createWindow);

app.on('window-all-closed', () => {
    app.quit();
});

app.on('before-quit', () => {
    win = null;
});
