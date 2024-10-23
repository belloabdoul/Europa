# Europa

Europa is a desktop application built with the frameworks angular and electron and the components libraries material UI
and the Ionic on the frontend; and .NET on the backend to find duplicates file with a cryptographic hash (Blake3) and
similar images with perceptual hashes (DCT based hash, difference hash and block mean hash).

## Built with

- C# / .NET on the backend
- Angular, TypeScript, Electron frameworks on the frontend
- Material UI / Ionic as components libraries
- Qdrant as a vector database
- The Blake3 cryptographic algorithm for duplicate files
- MagicScaler, NetVips and libRaw for reading images

## Requirements

- Qdrant database which you can download [here](https://github.com/qdrant/qdrant/releases)
- .NET 9 SDK (including .NET runtime)
- npm

## Installation process

- Clone this repository locally:

```sh
https://github.com/belloabdoul/Europa.git
```

- Launch the qdrant local instance

- Restore the solution dependencies and run the .NET API project. From the root of the solution, do:

```sh
dotnet restore
cd Api
dotnet run -c Release
```

- Restore the UI dependencies and run the UI project. From the root of the solution, do:

```sh
cd web
npm install --legacy-peer-deps
npm start
```

- Done. The UI should be running, the API should be running, and you can start using the application.

## To do

- ~~Add the choice of perceptual hash in the UI.~~
- Package the UI with electron-forge or electron-builder and the API.
- Add unit test.

[//]: # (## Bugs and feature requests)

[//]: # (Have a bug or a feature request? Please first read)

[//]: # (the [issue guidelines]&#40;https://github.com/harshbanthiya/Paint_Stable_Diffusion/blob/master/CONTRIBUTING.md&#41; and search)

[//]: # (for existing and closed issues. If your problem or idea is not addressed)

[//]: # (yet, [please open a new issue]&#40;https://github.com/harshbanthiya/Paint_Stable_Diffusion/issues/new&#41;.)