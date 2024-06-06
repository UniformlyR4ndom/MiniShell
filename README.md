# MiniShell
A reverse shell for Windows systems that brings some convenience features.
Among them are:
+ file transfer
+ automatic reconnection attempt on connection loss
+ easy duplication of established shells
+ easy choice/switch between cmd and powershell command interpreter
+ features can be chosen at build time (can be stripped down to a minimal shell without extra features)


# Build
+ using Visual Studio 2022

## .NET executable
+ preferably set target framework at `net46` since this should work for most Windows 10 and newer system out of the box

### Build release version (recommended)
+ smaller than debug
+ debug output disabled (doesn't matter much for regular use)
```
MSBuild.exe .\MiniShell.sln /p:Configuration=Release
```
### Build debug version
```
MSBuild.exe .\MiniShell.sln /p:Configuration=Debug
```

## Native executable
+ should work on Windows systems without .NET runtime (pre Windows 10), but binaries are much larger than .NET binaries
+ uses the ahead-of-time compilation feature of .NET 7+ (https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/?tabs=net7%2Cwindows)
+ set target framework to `net8` (in `MiniShell.csproj`) before building


### Build release version (recommended)
+ recommended since it saves about half in size
```
dotnet publish -r win-x64 -c Release -f net8 -p:PublishAot=true --self-contained
```

### Build debug version
```
dotnet publish -r win-x64 -c Debug -f net8 -p:PublishAot=true --self-contained
```