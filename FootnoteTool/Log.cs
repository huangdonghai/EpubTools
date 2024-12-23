using AngleSharp.Dom;
using System;
using System.IO;

namespace EpubTools;

public class Log
{
    static string t = "";
    static string level = "";
    public static void log_tab(string s)
    {
        log(level + s);
    }
    public static void log(string s)
    {
        t += s + "\r\n";
        Console.WriteLine(s);
    }
    public static void Save(string path)
    {
        File.WriteAllText(path, t);
    }

    public static void log(string filename, IElement node, string msg)
    {
        log(filename + ":" + node.SourceReference.Position.Line + ":" + node.SourceReference.Position.Column + ":" + node.OuterHtml + ": " + msg);
    }
}




