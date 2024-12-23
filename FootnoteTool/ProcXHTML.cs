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
    public IElement srcNoteId;
    public IElement dstParentNode;
    public IElement dstNoteId;
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

    string m_filename;

    List<NoteEntry> m_notes = [];
    List<IElement> m_orphanNotes = []; // notes without a corresponding link

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
        m_filename = Path.GetFileName(filename);
        Log.log("-------" + m_filename + "---------");
        var text = File.ReadAllText(filename);
#if false
        //Use the default configuration for AngleSharp
        IConfiguration config = Configuration.Default;

        IBrowsingContext context = BrowsingContext.New(config);
        IHtmlParser parser = context.GetService<IHtmlParser>();
#else
        var parser = new HtmlParser(new HtmlParserOptions
        {
            IsKeepingSourceReferences = true,
        });
#endif

        IDocument document = parser.ParseDocument(text);

        PrepareNotes(document, notePosition);

        //ProcessNotes(document, notePosition);

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
            text = match.Groups[1].Value;
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

            // check error
            if (originalOrder == 0)
            {
                Log.log(m_filename, noteid, "Error: noteid is invalid");
                continue;
            }

            // add new note entry
            if (parent.ClassName != NoteCssClass)
            {
                var entry = new NoteEntry();
                entry.srcParentNode = parent;
                entry.srcNoteId = noteid;
                entry.hasLinkTag = false;
                entry.originalOrder = originalOrder;
                entry.newOrder = 0;
                entry.wholeFileOrder = wholeFileIdCount++;

                // check original order is valid
                if (parent != lastParent)
                {
                    if (lastParent == null)
                    {
                        if (entry.originalOrder != 1)
                        {
                            Log.log(m_filename, noteid, "id is out of order, should be 1 but really is " + entry.originalOrder);
                        }
                    } else
                    {
                        var lastEntry = m_notes[m_notes.Count - 1];
                        if (entry.originalOrder != lastEntry.originalOrder + 1 && entry.originalOrder != 1)
                        {
                            Log.log(m_filename, noteid, $"id is out of order, should be 1 or {lastEntry.originalOrder + 1} but really is {entry.originalOrder} ");
                        }
                    }
                }
                else
                {
                    var lastEntry = m_notes[m_notes.Count - 1];
                    if (entry.originalOrder != lastEntry.originalOrder + 1)
                    {
                        Log.log(m_filename, noteid, $"id is out of order, should be {lastEntry.originalOrder + 1} but really is {entry.originalOrder} ");
                        // if the order is lower than last entry, ignore it
                        if (entry.originalOrder <= lastEntry.originalOrder)
                        {
                            m_orphanNotes.Add(noteid);
                            continue;
                        }
                    }
                }

                // check new note position
                if (notePosition == NotePosition.Inline)
                {
                    // DO NOTHING
                }
                else if (notePosition == NotePosition.BlockEnd)
                {
                    entry.newOrder = curBlockCount + 1;
                    curBlockCount++;
                }
                else if (notePosition == NotePosition.PageEnd)
                {
                    entry.newOrder = wholeFileIdCount;
                }

                if (entry.newOrder != entry.originalOrder)
                    m_dirty = true;

                m_notes.Add(entry);

                if (lastParent != parent)
                {
                    lastParent = parent;
                    curBlockCount = 1;
                }
            }
            // this is a dst node, try link to src node id
            else
            {
                // check duplicate noteid
                if (parent == lastParent)
                {
                    Log.log(m_filename, noteid, $"Waring: duplicated note id {originalOrder}");
                    continue;
                }

                // check limits
                if (lastDstCount >= m_notes.Count)
                {
                    Log.log(m_filename, noteid, $"Waring: too many note id {originalOrder}");
                    continue;
                }

                var entry = m_notes[lastDstCount++];
                if (entry.originalOrder != originalOrder)
                {
                    Log.log(m_filename, noteid, $"Warning: noteid order not match, expect {originalOrder} but really is {entry.originalOrder}");

                    // try to find the match entry
                    while (lastDstCount < m_notes.Count)
                    {
                        if (m_notes[lastDstCount].originalOrder == originalOrder)
                        {
                            entry = m_notes[lastDstCount++];
                            break;
                        }
                        else
                        {
                            lastDstCount++;
                        }
                    }
                }

                if (entry.originalOrder == originalOrder)
                {
                    entry.dstParentNode = parent;
                    entry.dstNoteId = noteid;
                    lastParent = parent;
                }
                else
                {
                    m_orphanNotes.Add(noteid);
                    Log.log(m_filename, noteid, $"Waring: noteid {originalOrder} not found");
                }
            }
        }
    }

    private void ProcessNotes(IDocument document, NotePosition notePosition)
    {
        foreach (var entry in m_notes)
        {
            if (notePosition == NotePosition.Inline)
            {
                entry.srcNoteId.InnerHtml = (entry.dstParentNode.TextContent);
                entry.srcNoteId.ClassName = NoteCssClass;
            }
        }
    }
}