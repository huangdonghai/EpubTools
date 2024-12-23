using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
namespace EpubTools;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Epub BlockEnd Tool");
        if (args.Length > 0)
        {
            if (Path.GetExtension(args[0]).ToLower() == ".epub" && File.Exists(args[0]))
            {
                Util.DeleteDir("temp");
                Util.Unzip(args[0], "temp");

                List<string> css = new List<string>();
                Util.ForeachFile("temp",
                (path) =>
                {
                    var ext = Path.GetExtension(path).ToLower();
                    if (ext == ".xhtml" || ext == ".html")
                    {
                        var x = new ProcXHTML(path, NotePosition.Inline);
                    }
                }
                );
                foreach (string p in css)
                {
                }
                Util.DeleteEmptyDir("temp");
                string outname = Path.GetFileNameWithoutExtension(args[0]) + " [FootnoteTool].epub";
                outname=Path.Combine(Path.GetDirectoryName(args[0]), outname);
                Util.Packup(outname);
                Util.DeleteDir("temp");

                string logname = Path.GetFileNameWithoutExtension(args[0]) + " [FootnoteTool].log";
                logname = Path.Combine(Path.GetDirectoryName(args[0]), logname);
                Log.Save(logname);

            }
            else
                Console.WriteLine("Invalid Input File");
        }
        else
            Console.WriteLine("Usage: <epub file>");

    }


}
