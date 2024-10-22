﻿using CommandLine;
namespace JobSync
{
    // This class defines the command-line options for the application.
    public class Parameters
    {
        [Option('l', "logFilePath", Required = true, HelpText = "Path to the log file.")]
        public string LogFilePath { get; set; } = string.Empty;

        [Option('v', "verbose", Default = 1, HelpText = "Set verboseness of output. [0-2]")]
        public int Verbose { get; set; }

        [Option('i', "interval", Required = true, HelpText = "Interval in milliseconds.")]
        public int Interval { get; set; }

        [Option('f', "fragile", Default = false, HelpText = "Stops synchronization on first error.")]
        public bool Fragile { get; set; }

        [Option('s', "sourcePath", Required = true, HelpText = "Path to the source directory.")]
        public string SourcePath { get; set; } = string.Empty;

        [Option('r', "replicaPath", Required = true, HelpText = "Path to the replica directory.")]
        public string ReplicaPath { get; set; } = string.Empty;

        [Option('c', "comparator", Default = "MD5", HelpText = "Comparator used to ensure files identity (MD5/Binary/SHA256/NONE(Only check size))")]
        public string Comparator { get; set; } = "MD5";
    }

    // This class is responsible for parsing command-line arguments.
    public class CommandLineProcessor
    {
        // Parses the command-line arguments into a Parameters object.
        public static Parameters? ParseArguments(string[] args)
        {
            Parameters? parameters = null;
            _ = Parser.Default.ParseArguments<Parameters>(args)
                .WithParsed(opts => parameters = opts); // If parsing succeeds, the options are assigned to the parameters object.
            return parameters; // Returns the parameters object or null if parsing fails.
        }
    }
}
