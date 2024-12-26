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
    Undefined,
    Inline,
    BlockEnd,
    MultiBlockEnd,
    BlockEndMulti,
    PageEnd
}

public enum NoteReposition
{
    DontMove,
    Inline,
    BlockEnd,
    BlockEndOnlySourceIsPageEnd,
    PartEnd,
    PartEndOnlySourceIsPageEnd,
    PageEnd
}

class NoteEntry
{
    public IElement srcBlockElement;
    public IElement srcNoteId;
    public IElement dstNoteElement;
    public IElement dstNoteId;
    public bool isLinkTag = false;
    public bool isInlineNode = false;
    public int originNum = 0;
    public int originClusterNum = 0;
    public int newNum = 0;
    public int wholeFileNum = 0;
}



public class ProcXHTML
{
    const string NoteId = @"①②③④⑤⑥⑦⑧⑨⑩⑪⑫⑬⑭⑮⑯⑰⑱⑲⑳㉑㉒㉓㉔㉕㉖㉗㉘㉙㉚㉛㉜㉝㉞㉟㊱㊲㊳㊴㊵㊶㊷㊸㊹㊺㊻㊼㊽㊾㊿" +
        "ⒶⒷⒸⒹⒺⒻⒼⒽⒾⒿⓀⓁⓂⓃⓄⓅⓆⓇⓈⓉⓊⓋⓌⓍⓎⓏⓐⓑⓒⓓⓔⓕⓖⓗⓘⓙⓚⓛⓜⓝⓞⓟⓠⓡⓢⓣⓤⓥⓦⓧⓨⓩ";
    const string AltNodeId = @"㊀㊁㊂㊃㊄㊅㊆㊇㊈㊉";
    static Dictionary<char, int> m_noteIdDict = [];

    const string NoteClass = "note";
    const string NoteIdClass = "noteid";
    const string PartClass = "partname";

    Regex RegId = new Regex(@"[\(\[〔【](\d+|[一二三四五六七八九〇]+)[\)\]〕】]");

    string m_filename;

    List<NoteEntry> m_notesPaired = [];
    List<IElement> m_orphanNotes = []; // notes without a corresponding link

    bool m_dirty = false;
    NotePosition m_notePosition = NotePosition.Undefined;
    int m_maxNoteNum = 0;

    public int MaxNoteNum => m_maxNoteNum;

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

    public ProcXHTML(string filename, NoteReposition reposition)
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

        PrepareNotes(document, reposition);

        ProcessNotes(document, reposition);

        if (m_dirty)
            File.WriteAllText(filename, document.DocumentElement.OuterHtml);
    }

    void DeterminCurrentNotePosition()
    {

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

    private int GetNoteNum(string text)
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

    private void PrepareNotes(IDocument document, NoteReposition notePosition)
    {
        // dict help find srcNoteId and dstNote
        Dictionary<int, NoteEntry> entryDict = [];
        NoteEntry lastEntry = null;
        NoteEntry lastMatchedEntry = null;

        int wholeFileNum = 0;

        int originClusterNum = 0;

        bool isPageEnd = true;

        var noteids = document.QuerySelectorAll("." + NoteIdClass);

        if (noteids.Length == 0)
            return;

        bool lastIsNote = false;

        foreach (var noteid in noteids)
        {
            var parent = GetParentBlockElement(noteid);
            if (parent == null)
            {
                Log.log("Error: noteid has no parent block element");
                continue;
            }

            int originNum = GetNoteNum(noteid.TextContent);

            // check error
            if (originNum == 0)
            {
                Log.log(m_filename, noteid, "Error: noteid is invalid");
                continue;
            }

            // add new note entry
            if (parent.ClassName != NoteClass)
            {
                if (originNum == 0)
                {
                    Log.log(m_filename, noteid, "Error: noteid is invalid");
                    continue;
                }

                if (lastMatchedEntry != null)
                {
                    originClusterNum++;
                    lastMatchedEntry = null;
                }

                var entry = new NoteEntry();
                entry.srcBlockElement = parent;
                entry.srcNoteId = noteid;
                entry.isLinkTag = false;
                entry.originNum = originNum;
                entry.originClusterNum = originClusterNum;
                entry.newNum = 0;
                entry.wholeFileNum = wholeFileNum++;

                // check if number is duplicate
                if (entryDict.ContainsKey(originNum))
                {
                    Log.log(m_filename, noteid, "Error: noteid is duplicated");
                    m_orphanNotes.Add(noteid);
                }

                entryDict[originNum] = entry;

                IElement lastParent = lastEntry?.srcBlockElement;

                // check original order is valid
                if (parent != lastParent)
                {
                    if (lastParent == null)
                    {
                        if (entry.originNum != 1)
                        {
                            Log.log(m_filename, noteid, "src id is out of order, should be 1 but really is "
                                                        + entry.originNum);
                        }
                    } else
                    {
                       if (entry.originNum != lastEntry.originNum + 1 && entry.originNum != 1)
                        {
                            Log.log(m_filename, noteid, "src id is out of order, should be 1 or "
                                                        + (lastEntry.originNum + 1) + "but really is "
                                                        + entry.originNum);
                        }
                    }
                }
                else
                {
                   if (entry.originNum != lastEntry.originNum + 1)
                    {
                        Log.log(m_filename, noteid, "src id is out of order, should be "
                                                    + (lastEntry.originNum + 1) + " but really is " 
                                                    + entry.originNum);
                    }
                }

                lastEntry = entry;
                lastMatchedEntry = null;

                if (m_notesPaired.Count > 0)
                {
                    isPageEnd = false;
                }
            }
            // this is a dst node, try link to src node id
            else
            {
                // check duplicate noteid
                if (parent == lastMatchedEntry?.dstNoteElement)
                {
                    Log.log(m_filename, parent, $"Waring: duplicated dst note id {originNum}");
                    m_orphanNotes.Add(noteid);
                    continue;
                }

                if (lastMatchedEntry != null && lastMatchedEntry.originNum + 1 != originNum)
                {
                    Log.log(m_filename, parent, $"dst noteId is out of order");
                }

                if (!entryDict.ContainsKey(originNum))
                {
                    Log.log(m_filename, parent, $"Waring: src note id {originNum} not found");
                    m_orphanNotes.Add(noteid);
                    continue;
                }

                var entry = entryDict[originNum];
                entryDict.Remove(originNum);
                entry.dstNoteElement = parent;
                entry.dstNoteId = noteid;

                m_notesPaired.Add(entry);

                lastMatchedEntry = entry;
            }
        }

        if (isPageEnd)
        {
            m_notePosition = NotePosition.PageEnd;
        }
    }

    private void ProcessNotes(IDocument document, NoteReposition reposition)
    {
        if (reposition == NoteReposition.BlockEndOnlySourceIsPageEnd && m_notePosition == NotePosition.PageEnd)
        {
            RepositionNoteToBlockEnd();
            return;
        }

        if (reposition == NoteReposition.PartEndOnlySourceIsPageEnd && m_notePosition == NotePosition.PageEnd)
        {
            RepositionNoteToPartEnd();
            return;
        }

        if (reposition == NoteReposition.PartEnd)
        {
            RepositionNoteToPartEnd();
            return;
        }

        RenumberingNotes();
    }

    string GetNoteId(int num)
    {
        if (num < 1 || num > NoteId.Length)
            return "[" + num + "]";

        return NoteId[num - 1].ToString();
    }

    private void RepositionNoteToBlockEnd()
    {
        Log.log("repositioning " + m_filename);
        NoteEntry lastEntry = null;
        IElement blockLastNote = null; // new note will append to this note
        int blockNoteNum = 1; // note num start from 1
        int maxNoteNum = 0;

        foreach (var entry in m_notesPaired)
        {
            // new block
            if (entry.srcBlockElement != lastEntry?.srcBlockElement)
            {
                blockNoteNum = 1;
                entry.srcBlockElement.After(entry.dstNoteElement);
                entry.srcNoteId.TextContent = GetNoteId(blockNoteNum);
                entry.dstNoteId.TextContent = GetNoteId(blockNoteNum);
            }
            else
            {
                blockLastNote.After(entry.dstNoteElement);
                entry.srcNoteId.TextContent = GetNoteId(blockNoteNum);
                entry.dstNoteId.TextContent = GetNoteId(blockNoteNum);
            }
            maxNoteNum = blockNoteNum > maxNoteNum ? blockNoteNum : maxNoteNum;
            lastEntry = entry;
            blockLastNote = entry.dstNoteElement;
            blockNoteNum++;
        }

        m_maxNoteNum = maxNoteNum > m_maxNoteNum ? maxNoteNum : m_maxNoteNum;
        m_dirty = true;
    }

    static IElement GetParentPartElement(IElement node)
    {
        for (var next = node.NextElementSibling; next != null; next = next.NextElementSibling)
        {
            if (next.ClassName == PartClass)
                return next;
        }
        return null;
    }

    private void RepositionNoteToPartEnd()
    {
        Log.log("repositioning " + m_filename);
        NoteEntry lastEntry = null;
        IElement lastPartElement = null;
        IElement blockLastNote = null; // new note will append to this note
        int blockNoteNum = 1; // note num start from 1
        int maxNoteNum = 0;

        foreach (var entry in m_notesPaired)
        {
            var curPart = GetParentPartElement(entry.srcBlockElement);
            // new block
            if (curPart != lastPartElement)
            {
                blockNoteNum = 1;
                lastPartElement = curPart;

                if (lastPartElement == null)
                {
                    entry.srcBlockElement.After(entry.dstNoteElement);
                    entry.srcNoteId.TextContent = GetNoteId(blockNoteNum);
                    entry.dstNoteId.TextContent = GetNoteId(blockNoteNum);
                } else
                {
                    lastPartElement.Before(entry.dstNoteElement);
                    entry.srcNoteId.TextContent = GetNoteId(blockNoteNum);
                    entry.dstNoteId.TextContent = GetNoteId(blockNoteNum);
                }
            }
            else
            {
                blockLastNote.After(entry.dstNoteElement);
                entry.srcNoteId.TextContent = GetNoteId(blockNoteNum);
                entry.dstNoteId.TextContent = GetNoteId(blockNoteNum);
            }
            maxNoteNum = blockNoteNum > maxNoteNum ? blockNoteNum : maxNoteNum;
            lastEntry = entry;
            blockLastNote = entry.dstNoteElement;
            blockNoteNum++;
        }

        m_maxNoteNum = maxNoteNum > m_maxNoteNum ? maxNoteNum : m_maxNoteNum;
        m_dirty = true;
    }

    private void RenumberingNotes()
    {
        Log.log("renumbering " + m_filename);

        int lastClusterNum = -1;
        int clusterNoteNum = 1; // note num start from 1
        int maxNoteNum = 0;

        foreach (var entry in m_notesPaired)
        {
            // new block
            if (entry.originClusterNum != lastClusterNum)
            {
                clusterNoteNum = 1;
                lastClusterNum = entry.originClusterNum;
            }

            entry.srcNoteId.TextContent = GetNoteId(clusterNoteNum);
            entry.dstNoteId.TextContent = GetNoteId(clusterNoteNum);

            maxNoteNum = clusterNoteNum > maxNoteNum ? clusterNoteNum : maxNoteNum;
            clusterNoteNum++;
        }

        m_maxNoteNum = maxNoteNum > m_maxNoteNum ? maxNoteNum : m_maxNoteNum;
        m_dirty = true;
    }
}