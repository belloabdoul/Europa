export interface IElectronAPI {
  selectDirectory: () => Promise<string>;
  openFileInDefaultApplication: (path: string) => Promise<string>;
  openFileLocation: (path: string) => Promise<void>;
}
