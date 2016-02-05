using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Monitor.Core.Utilities;

namespace BinObjJunction
{
    static class Program
    {
        static bool HasDotFolder(this string str)
        {
            return str.Contains("\\.");
        }

        static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Usage:");
                Console.Error.WriteLine("  BinObjJunction.exe <solution path> <junctions root path>");
                return 1;
            }

            string solution = args[0];
            if (solution.HasDotFolder())
            {
                Console.Error.WriteLine("<solution path> must not contain a \\.dot folder (like .git or .svn)");
                return 1;
            }

            string junctionsRoot = args[1];

            try
            {
                Work(solution, junctionsRoot);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.ToString());
                return 1;
            }

            return 0;
        }

        static readonly Regex s_outputPathRegex = new Regex("<OutputPath>([^<]+)</OutputPath>");

        static void Work(string solution, string junctionsRoot)
        {
            HashSet<string> tempPaths = new HashSet<string>();

            foreach (string projFile in Directory.GetFiles(solution, "*.csproj", SearchOption.AllDirectories))
            {
                string projFolder = Path.GetDirectoryName(projFile);

                if (projFolder.HasDotFolder())
                    continue;

                string projXml = File.ReadAllText(projFile);

                IEnumerable<Match> matches = s_outputPathRegex.Matches(projXml).Cast<Match>();
                foreach (string path in matches.FindBinPaths(projFolder))
                    tempPaths.Add(path.TrimEnd(Path.DirectorySeparatorChar));

                tempPaths.Add(Path.Combine(projFolder, "obj"));
            }

            foreach (string sln in Directory.GetFiles(solution, "*.sln", SearchOption.AllDirectories))
            {
                tempPaths.Add(Path.Combine(Path.GetDirectoryName(sln), "obj"));
            }

            foreach (string path in tempPaths)
            {
                string relativePath = MakeRelativePath(solution, path);
                string targetPath = Path.Combine(junctionsRoot, relativePath);

                bool correctJunctionExists = false;

                if (Directory.Exists(path))
                {
                    if (JunctionPoint.Exists(path))
                    {
                        string existingTarget = JunctionPoint.GetTarget(path);
                        if (existingTarget == targetPath)
                            correctJunctionExists = true;
                        else
                            continue;
                    }
                    else if (IsDirectoryEmpty(path))
                        Directory.Delete(path, true);
                }

                Directory.CreateDirectory(targetPath);

                if (!correctJunctionExists)
                {
                    Console.WriteLine("Creating junction:");
                    Console.WriteLine("  from " + path);
                    Console.WriteLine("    to " + targetPath);
                    JunctionPoint.Create(path, targetPath, false);
                }
            }
        }

        static IEnumerable<string> FindBinPaths(this IEnumerable<Match> matches, string projFolder)
        {
            foreach (Match match in matches)
            {
                string path = match.Groups[1].Value;
                path = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

                path = Path.Combine(projFolder, path);

                // this expands ..
                path = Path.GetFullPath(path);

                if (path.HasDotFolder())
                    continue;

                string[] parts = path.Split(Path.DirectorySeparatorChar);
                for (int i = 2; i <= 4; i++)
                {
                    if (parts.Length <= i)
                        break;

                    int binIdx = parts.Length - i;
                    if (!parts[binIdx].Equals("bin", StringComparison.InvariantCultureIgnoreCase))
                        continue;

                    path = string.Join(Path.DirectorySeparatorChar.ToString(), parts.Take(binIdx + 1));
                    break;
                }

                yield return path;
            }
        }

        // http://stackoverflow.com/a/340454/823663
        /// <summary>
        /// Creates a relative path from one file or folder to another.
        /// </summary>
        /// <param name="fromPath">Contains the directory that defines the start of the relative path.</param>
        /// <param name="toPath">Contains the path that defines the endpoint of the relative path.</param>
        /// <returns>The relative path from the start directory to the end path.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="UriFormatException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        static String MakeRelativePath(String fromPath, String toPath)
        {
            if (String.IsNullOrEmpty(fromPath)) throw new ArgumentNullException("fromPath");
            if (String.IsNullOrEmpty(toPath)) throw new ArgumentNullException("toPath");

            Uri fromUri = new Uri(fromPath);
            Uri toUri = new Uri(toPath);

            if (fromUri.Scheme != toUri.Scheme) { return toPath; } // path can't be made relative.

            Uri relativeUri = fromUri.MakeRelativeUri(toUri);
            String relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            if (toUri.Scheme.ToUpperInvariant() == "FILE")
            {
                relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            }

            return relativePath;
        }

        static bool IsDirectoryEmpty(string path)
        {
            if (Directory.EnumerateFiles(path).Any())
                return false;

            if (Directory.EnumerateDirectories(path).Any(d => JunctionPoint.Exists(d) || !IsDirectoryEmpty(d)))
                return false;

            return true;
        }
    }
}
