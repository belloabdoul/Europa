import { app, BrowserWindow, dialog, ipcMain, screen, shell } from 'electron';
import * as path from 'path';
import * as fs from 'fs';
import * as url from 'url';
import debug from 'electron-debug';
const __filename = url.fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
let win = null;
const args = process.argv.slice(1), serve = args.some((val) => val === '--serve');
// Create application
function createWindow() {
    const size = screen.getPrimaryDisplay().workAreaSize;
    // Create the browser window.
    win = new BrowserWindow({
        x: 0,
        y: 0,
        width: size.width,
        height: size.height,
        minHeight: size.height / 2,
        minWidth: size.width / 2,
        webPreferences: {
            allowRunningInsecureContent: serve,
            backgroundThrottling: false,
            safeDialogs: true,
            preload: path.join(__dirname, 'preload.cjs'),
        },
    });
    if (serve) {
        debug();
        win.loadURL('http://localhost:4200');
    }
    else {
        // Path when running electron executable
        let pathIndex = './index.html';
        if (fs.existsSync(path.join(__dirname, '../dist/index.html'))) {
            // Path when running electron in local folder
            pathIndex = '../dist/index.html';
        }
        const url = new URL(path.join('file:', __dirname, pathIndex));
        win.loadURL(url.href);
    }
    // Emitted when the window is closed.
    win.on('closed', () => {
        // Dereference the window object, usually you would store window
        // in an array if your app supports multi windows, this is the time
        // when you should delete the corresponding element.
        win = null;
    });
    return win;
}
try {
    app.commandLine.appendSwitch('ignore-certificate-errors');
    app.commandLine.appendSwitch('allow-insecure-localhost', 'true');
    // This method will be called when Electron has finished
    // initialization and is ready to create browser windows.
    // Some APIs can only be used after this event occurs.
    // Added 400 ms to fix the black background issue while using transparent window. More detais at https://github.com/electron/electron/issues/15947
    app.on('ready', () => setTimeout(function () {
        // Launch the os folder chooser
        ipcMain.handle('dialog:selectDirectory', () => {
            const filePaths = dialog.showOpenDialogSync({
                properties: ['openDirectory', 'showHiddenFiles', 'dontAddToRecent'],
            });
            if (typeof filePaths != 'undefined') {
                return filePaths[0];
            }
            return '';
        });
        // Open the file in the default application
        ipcMain.handle('shell:openFileInDefaultApplication', async (_event, [path]) => await shell.openPath(path));
        ipcMain.handle('shell:openFileLocation', (_event, [path]) => shell.showItemInFolder(path));
        createWindow();
    }, 400));
    // Quit when all windows are closed.
    app.on('window-all-closed', () => {
        // On OS X it is common for applications and their menu bar
        // to stay active until the user quits explicitly with Cmd + Q
        if (process.platform !== 'darwin') {
            app.quit();
        }
    });
    app.on('activate', () => {
        // On OS X it's common to re-create a window in the app when the
        // dock icon is clicked and there are no other windows open.
        if (win === null) {
            createWindow();
        }
    });
}
catch (e) {
    // Catch Error
    // throw e;
}
//# sourceMappingURL=main.js.map