using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static MiniShell.CommandInterpreter;

namespace MiniShell {

    class DataHandler {
        public OutputCallback callback;
        public DataReceivedEventHandler evtHandler;

        public DataHandler(OutputCallback callback, DataReceivedEventHandler handler) {
            this.callback = callback;
            this.evtHandler = handler;
        }
    }

    abstract class CommandInterpreter {
        public delegate void OutputCallback(string output);


        public abstract bool start(string workingDir = null);

        public abstract bool exit();

        public abstract bool istRunning();

        public abstract string getCwd();

        public abstract Process getProcess();

        public abstract void writeStdin(string input);

        public abstract void addStdoutCallback(OutputCallback callback);
        public abstract void removeStdoutCallback(OutputCallback callback);
        public abstract void addStderrCallback(OutputCallback callback);
        public abstract void removeStderrCallback(OutputCallback callback);
    }
}
