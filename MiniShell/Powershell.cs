using System.Diagnostics;

namespace MiniShell {
    internal class Powershell : CommandInterpreter {
        private List<DataHandler> stdoutHandlers = new List<DataHandler>();
        private List<DataHandler> stderrHandlers = new List<DataHandler>();

        private bool started = false;
        private Process shell = null;
        private bool shellPassthroug = true;

        public Powershell() { }

        public override bool start(string workingDir = null) {
            if (started) {
                return false;
            }

            Process shell = new Process();
            if (workingDir != null) {
                shell.StartInfo = new ProcessStartInfo("powershell.exe", $"-ep bypass -noexit -c \"cd {workingDir}\"");
            } else {
                shell.StartInfo = new ProcessStartInfo("powershell.exe", $"-ep bypass -noexit");
            }
            shell.StartInfo.CreateNoWindow = true;
            shell.StartInfo.UseShellExecute = false;
            shell.StartInfo.RedirectStandardInput = true;
            shell.StartInfo.RedirectStandardOutput = true;
            shell.StartInfo.RedirectStandardError = true;
            this.shell = shell;

            shell.Start();
            shell.BeginOutputReadLine();
            shell.BeginErrorReadLine();

            started = true;
            return true;
        }

        public override bool exit() {
            try {
                this.shell.Kill();
                this.shell.Close();
                return true;
            } catch {
                return false;
            }
        }

        public override bool istRunning() {
            return !this.shell.HasExited;
        }

        public override string getCwd() {
            string cwd = null;
            bool done = false;
            int cnt = 0;

            var outputCallback = new OutputCallback((string output) => {
                Console.WriteLine(output);
                if (cnt++ == 4) {
                    cwd = output;
                    done = true;
                }
            });
            this.addStdoutCallback(outputCallback);
            this.shellPassthroug = false;
            this.writeStdin("pwd\n");
            while (!done) {
                System.Threading.Thread.Sleep(100);
            }
            this.shellPassthroug = true;
            this.removeStdoutCallback(outputCallback);
            return cwd;
        }

        public override Process getProcess() {
            return this.shell;
        }

        public override void writeStdin(string input) {
            this.shell.StandardInput.Write(input);
        }

        public override void addStdoutCallback(OutputCallback callback) {
            DataReceivedEventHandler evtHandler = new DataReceivedEventHandler((sender, evt) => {
                callback(evt.Data);
            });
            DataHandler handler = new DataHandler(callback, evtHandler);
            this.stdoutHandlers.Add(handler);
            this.shell.OutputDataReceived += evtHandler;
        }

        public override void removeStdoutCallback(OutputCallback callback) {
            DataHandler handler = this.stdoutHandlers.Find(item => item.callback == callback);
            this.shell.OutputDataReceived -= handler.evtHandler;
            this.stdoutHandlers.Remove(handler);
        }

        public override void addStderrCallback(OutputCallback callback) {
            DataReceivedEventHandler evtHandler = new DataReceivedEventHandler((sender, evt) => {
                callback(evt.Data);
            });
            DataHandler handler = new DataHandler(callback, evtHandler);
            this.stderrHandlers.Add(handler);
            this.shell.ErrorDataReceived += evtHandler;
        }

        public override void removeStderrCallback(OutputCallback callback) {
            DataHandler handler = this.stderrHandlers.Find(item => item.callback == callback);
            this.shell.ErrorDataReceived -= handler.evtHandler;
            this.stderrHandlers.Remove(handler);
        }
    }
}
