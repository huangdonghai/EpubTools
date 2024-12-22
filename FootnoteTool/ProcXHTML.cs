using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AngleSharp;

namespace EpubTools;

enum NotePosition
{
    DontMove,
    Inline,
    Footnote,
    Endnote
}



public class ProcXHTML
{
    const string NoteId = @"①②③④⑤⑥⑦⑧⑨⑩⑪⑫⑬⑭⑮⑯⑰⑱⑲⑳㉑㉒㉓㉔㉕㉖㉗㉘㉙㉚㉛㉜㉝㉞㉟㊱㊲㊳㊴㊵㊶㊷㊸㊹㊺㊻㊼㊽㊾㊿" +
        "ⒶⒷⒸⒹⒺⒻⒼⒽⒾⒿⓀⓁⓂⓃⓄⓅⓆⓇⓈⓉⓊⓋⓌⓍⓎⓏⓐⓑⓒⓓⓔⓕⓖⓗⓘⓙⓚⓛⓜⓝⓞⓟⓠⓡⓢⓣⓤⓥⓦⓧⓨⓩ";

    const string NoteCssClass = "note";

    string text;
    string filename;
    public bool contain_footnote = false;
    public List<string> css = new List<string>();

    Regex RegId = new Regex(@"[\(\[〔【](\d+)[\)\]〕】]");

    public ProcXHTML(string filename)
    {
        Log.log("-------" + Path.GetFileName(filename) + "---------");
        this.filename = filename;
        text = File.ReadAllText(filename);

        //Use the default configuration for AngleSharp
        IConfiguration config = Configuration.Default;

        IBrowsingContext context = BrowsingContext.New(config);
        IHtmlParser parser = context.GetService<IHtmlParser>();

        IDocument document = parser.ParseDocument(text);

        File.WriteAllText(filename, text);
    }
}