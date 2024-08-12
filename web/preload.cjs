const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('electronAPI', {
  selectDirectory() {
    return ipcRenderer.invoke('dialog:selectDirectory');
  },
  openFileInDefaultApplication(path) {
    return ipcRenderer.invoke('shell:openFileInDefaultApplication', [path]);
  },
  openFileLocation(path) {
    return ipcRenderer.invoke('shell:openFileLocation', [path]);
  },
});
