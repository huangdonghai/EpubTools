using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AngleSharp;
using System.Text;
using AngleSharp.Html.Dom;
using System.Diagnostics;

namespace EpubTools;

public enum NotePosition
{
    DontMove,
    Inline,
    BlockEnd,
    PageEnd
}

class NoteEntry
{
    public IElement srcParentNode;
    public IElement srcNodeId;
    public IElement dstParentNode;
    public IElement dstNodeId;
    public bool hasLinkTag = false;
    public bool isInlineNode = false;
    public int originalOrder = 0;
    public int newOrder = 0;
    public int wholeFileOrder = 0;
}



public class ProcXHTML
{
    const string NoteId = @"①②③④⑤⑥⑦⑧⑨⑩⑪⑫⑬⑭⑮⑯⑰⑱⑲⑳㉑㉒㉓㉔㉕㉖㉗㉘㉙㉚㉛㉜㉝㉞㉟㊱㊲㊳㊴㊵㊶㊷㊸㊹㊺㊻㊼㊽㊾㊿" +
        "ⒶⒷⒸⒹⒺⒻⒼⒽⒾⒿⓀⓁⓂⓃⓄⓅⓆⓇⓈⓉⓊⓋⓌⓍⓎⓏⓐⓑⓒⓓⓔⓕⓖⓗⓘⓙⓚⓛⓜⓝⓞⓟⓠⓡⓢⓣⓤⓥⓦⓧⓨⓩ";
    const string AltNodeId = @"㊀㊁㊂㊃㊄㊅㊆㊇㊈㊉";
    static Dictionary<char, int> m_noteIdDict = [];

    const string NoteCssClass = "note";
    const string NoteIdClass = "noteid";

    Regex RegId = new Regex(@"[\(\[〔【](\d+|[一二三四五六七八九〇]+)[\)\]〕】]");

    List<NoteEntry> m_notes = [];

    bool m_dirty = false;

    static ProcXHTML()
    {
        for (int i = 0; i < NoteId.Length; i++)
        {
            m_noteIdDict[NoteId[i]] = i + 1;
        }
        for (int i = 0; i < AltNodeId.Length; i++)
        {
            m_noteIdDict[AltNodeId[i]] = i + 1;
        }
    }

    public ProcXHTML(string filename, NotePosition notePosition)
    {
        Log.log("-------" + Path.GetFileName(filename) + "---------");
        var text = File.ReadAllText(filename);

        //Use the default configuration for AngleSharp
        IConfiguration config = Configuration.Default;

        IBrowsingContext context = BrowsingContext.New(config);
        IHtmlParser parser = context.GetService<IHtmlParser>();

        IDocument document = parser.ParseDocument(text);

        PrepareNotes(document, notePosition);

        ProcessNotes(document, notePosition);

        if (m_dirty)
            File.WriteAllText(filename, document.DocumentElement.OuterHtml);
    }

    static string ChineseToRoman(string text)
    {
        StringBuilder sb = new StringBuilder(text);

        sb.Replace("一", "1");
        sb.Replace("二", "2");
        sb.Replace("三", "3");
        sb.Replace("四", "4");
        sb.Replace("五", "5");
        sb.Replace("六", "6");
        sb.Replace("七", "7");
        sb.Replace("八", "8");
        sb.Replace("九", "9");
        sb.Replace("〇", "0");

        return sb.ToString().ToLower();
    }

    private int GetNoteOrder(string text)
    {
        text = text.Trim();

        var match = RegId.Match(text);
        if (match.Success)
        {
            if (text.Length == 0)
                return 0;
            if (text[0] >= '0' && text[0] <= '9')
                return int.Parse(text);

            return int.Parse(ChineseToRoman(text));
        }

        if (m_noteIdDict.ContainsKey(text[0]))
            return m_noteIdDict[text[0]];
        return 0;
    }

    static IElement GetParentBlockElement(IElement node)
    {
        IElement parent = node.ParentElement;
        while (parent != null)
        {
            if (parent is IHtmlParagraphElement || parent is IHtmlDivElement || parent is IHtmlHeadingElement)
                return parent;
            parent = parent.ParentElement;
        }
        return null;
    }

    private void PrepareNotes(IDocument document, NotePosition notePosition)
    {
        int wholeFileIdCount = 0;
        int lastDstCount = 0;

        IElement lastParent = null;
        int curBlockCount = 0;

        var noteids = document.QuerySelectorAll("." + NoteIdClass);

        if (noteids.Length == 0)
            return;

        foreach (var noteid in noteids)
        {
            var parent = GetParentBlockElement(noteid);
            if (parent == null)
            {
                Log.log("Error: noteid has no parent block element");
                continue;
            }

            int originalOrder = GetNoteOrder(noteid.TextContent);

            // add new note entry
            if (parent.ClassName != NoteCssClass)
            {
                var entry = new NoteEntry();
                entry.srcParentNode = parent;
                entry.srcNodeId = noteid;
                entry.hasLinkTag = false;
                entry.originalOrder = originalOrder;
                entry.newOrder = 0;
                entry.wholeFileOrder = wholeFileIdCount++;

                m_notes.Add(entry);

                // check new note position
                if (notePosition == NotePosition.Inline)
                {
                }
                else if (notePosition == NotePosition.BlockEnd)
                {
                    if (lastParent != parent)
                    {
                        lastParent = parent;
                        curBlockCount = 0;
                    }
                    entry.newOrder = curBlockCount + 1;
                    curBlockCount++;
                }
                else if (notePosition == NotePosition.PageEnd)
                {
                    entry.newOrder = wholeFileIdCount;
                }

                if (entry.newOrder != entry.originalOrder)
                    m_dirty = true;
            }
            // this is a dst node, try link to src node id
            else
            {
                var entry = m_notes[lastDstCount];
                if (entry.originalOrder != originalOrder)
                {
                    Log.log("Error: noteid order not match");
                }
                entry.dstParentNode = parent;
                entry.dstNodeId = noteid;

                lastDstCount++;
            }
        }
    }

    private void ProcessNotes(IDocument document, NotePosition notePosition)
    {
        foreach (var entry in m_notes)
        {
            if (notePosition == NotePosition.Inline)
            {
                entry.srcNodeId.InnerHtml = (entry.dstParentNode.TextContent);
                entry.srcNodeId.ClassName = NoteCssClass;
            }
        }
    }
}