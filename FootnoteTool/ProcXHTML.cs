using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace EpubTools;

public class ProcXHTML
{
    string text;
    string filename;
    public bool contain_footnote = false;
    public List<string> css = new List<string>();

    public ProcXHTML(string filename)
    {
        Log.log("-------" + Path.GetFileName(filename) + "---------");
        this.filename = filename;
        text = File.ReadAllText(filename);
        File.WriteAllText(filename, text);
    }
}