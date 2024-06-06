# MiniShell
A reverse shell for Windows systems that brings some convenience features.
Among them are:
+ file transfer
+ automatic reconnection attempt on connection loss
+ easy duplication of established shells
+ easy choice between cmd and powershell command interpreter
+ features can be chosen at build time (can be stripped down to a minimal shell without extra features)


# Build
+ using Visual Studio 2022

## .NET executable
+ preferably leave target framework at net46 since this should work most Windows 10 and newer system out of the box

### Build relese version
+ smaller than debug
+ debug output disabled (doesn't matter much for regula use)
```
MSBuild.exe .\MiniShell.sln /p:Configuration=Release
```
### Build debug version
```
MSBuild.exe .\MiniShell.sln /p:Configuration=Debug
```

## Native executable
+ should work on older Windows systems
+ uses the ahead-of-time compilation feature of .NET 7+ (https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/?tabs=net7%2Cwindows)
+ binaries are much larger than .NET binaries, but should work on Windows systems without .NET runtime

### Build release version
+ recommended since it saves about half in size
```
dotnet publish -r win-x64 -c Release -f net8 -p:PublishAot=true --self-contained
```

### Build debug version
```
dotnet publish -r win-x64 -c Debug -f net8 -p:PublishAot=true --self-contained
```