using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using ChessNEA.Engine;

namespace ChessNEA.Bench
{
    internal sealed record SuitePosition(string Name, string Category, string Fen);

    /// <summary>A versioned benchmark suite file (name;category;FEN per line, '#' comments).</summary>
    internal sealed class Suite
    {
        public string Name { get; private init; }
        public string Sha256 { get; private init; }
        public List<SuitePosition> Positions { get; } = new();

        public static Suite Load(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            var suite = new Suite
            {
                Name = Path.GetFileName(path),
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            };
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] parts = line.Split(';');
                if (parts.Length != 3) throw new FormatException($"Bad suite line: {line}");
                Position.FromFen(parts[2]); // validates
                suite.Positions.Add(new SuitePosition(parts[0], parts[1], parts[2]));
            }
            return suite;
        }
    }
}
