using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using MugsTech.Tts;
using MugsTech.Style;

/// <summary>
/// {Document:…} — the tag through every registration site (runtime parser,
/// TTS strip + stamp, tag-only unstamp), the stamp's words, timeline and
/// overlap geometry (pure), and the real card laid out with TMP: the marker
/// on the phrase (one stroke per line it spans), each stamp, and the stamp
/// never over the highlight on a short and a long passage.
/// </summary>
public static partial class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/Cards/Run Document Self-Tests")]
    public static void RunDocument()
    {
        var log = new Log();
        RunDocumentGroup(log);
        log.Report();
    }

    static void RunDocumentGroup(Log log)
    {
        Document_TagParsing(log);
        Document_StampUnstampRoundTrip(log);
        Document_StampWords(log);
        Document_Timeline(log);
        Document_Geometry(log);

        TMP_FontAsset savedFont = MugsTech.Style.VisualsRuntimeApplier.CardFontOverride;
        MugsTech.Style.VisualsRuntimeApplier.CardFontOverride = null;
        try
        {
            Document_Fonts(log);
            Document_CardLayout(log);
            Document_PhraseNotFound(log);
            Document_TimelineFrameByFrame(log);
        }
        catch (Exception e)
        {
            log.IsTrue(false, $"Document layout checks threw {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            MugsTech.Style.VisualsRuntimeApplier.CardFontOverride = savedFont;
            DestroyAutoAnimator();
        }
    }

    // -----------------------------------------------------------------------
    // The tag through the registration sites
    // -----------------------------------------------------------------------

    const string DocumentTag = "{Document:\"The attacker still holds about USD 2.4 billion in assets beyond issuer reach.\"," +
                               "\"beyond issuer reach\",\"TRM Labs\",Start,Report,Right}";

    static void Document_TagParsing(Log log)
    {
        string script =
            DocumentTag + " words\n" +
            "more words {Document:End}\n" +
            "{Document:\"The SEC alleges fraud.\",\"alleges fraud\",\"SEC complaint\",6,Allegation} later\n" +
            "{Document:\"Filed today.\",\"Filed\",\"Court\",Start,Filing,Left}\n" +
            "{Document:\"The court rules.\",\"rules\",\"Judge\",Start,Ruling}\n" +
            "{Document:\"No stamp here.\",\"stamp\",\"Src\",Start}\n" +
            "{Document:\"Side only.\",\"\",\"\",Start,Right}\n" +
            "{Document:\"stamped\",\"stamped\",\"x\",T=12.500,D=0,Report,Right}\n" +
            "{Document:\"Phrase missing.\",\"not in here\",\"Src\",Start}\n" +
            "{Document:\"only two\",\"fields\",Start}\n" +
            "{Document:\"bad\",\"keyword\",\"x\",Start,Proven}\n" +
            "{Document:\"wrong\",\"order\",\"x\",Start,Right,Report}\n" +
            "{Document:\"\",\"empty passage\",\"x\",Start}\n";
        var (clean, events) = ContentZoneTagParser.ParseContentTags(script, 30f);
        var cards = events.FindAll(e => e.cardType == ContentCardType.Document && !e.dismissesCard);
        var ends = events.FindAll(e => e.cardType == ContentCardType.Document && e.dismissesCard);

        log.AreEqual(8, cards.Count, "eight valid Document tags become cards (four malformed ones are skipped)");
        log.AreEqual(1, ends.Count, "{Document:End} closes a held Document");
        log.IsTrue(!clean.Contains("{Document"), "every Document tag — valid, End, malformed — is stripped from the text");

        ContentCardEvent first = cards.Find(e => e.tertiaryText == "TRM Labs");
        log.IsTrue(first != null && first.duration == 0f && first.documentStamp == DocumentStamp.Report &&
                   first.entryDirectionOverride == EntryDirection.FromRight && first.secondaryText == "beyond issuer reach",
            "the held tag keeps its passage, phrase, source, Report stamp and ,Right");
        ContentCardEvent timed = cards.Find(e => e.tertiaryText == "SEC complaint");
        log.IsTrue(timed != null && Mathf.Approximately(timed.duration, 6f) && timed.documentStamp == DocumentStamp.Allegation &&
                   timed.entryDirectionOverride == null, "a timed tag keeps its duration and the Allegation stamp");
        log.IsTrue(cards.Exists(e => e.documentStamp == DocumentStamp.Filing && e.entryDirectionOverride == EntryDirection.FromLeft),
            "Filing with ,Left");
        log.IsTrue(cards.Exists(e => e.documentStamp == DocumentStamp.Ruling), "Ruling");
        log.IsTrue(cards.Exists(e => e.primaryText == "No stamp here." && e.documentStamp == DocumentStamp.None),
            "no keyword → no stamp");
        log.IsTrue(cards.Exists(e => e.primaryText == "Side only." && e.documentStamp == DocumentStamp.None &&
                                     e.entryDirectionOverride == EntryDirection.FromRight && e.secondaryText == "" && e.tertiaryText == ""),
            "a side with no keyword, and an empty phrase and source");
        ContentCardEvent stamped = cards.Find(e => e.primaryText == "stamped");
        log.IsTrue(stamped != null && Mathf.Approximately(stamped.triggerTime, 12.5f) && stamped.documentStamp == DocumentStamp.Report &&
                   stamped.entryDirectionOverride == EntryDirection.FromRight, "a stamped tag (T=…, D=0, Report, Right) fires at its T=");
        log.IsTrue(cards.Exists(e => e.primaryText == "Phrase missing."),
            "a phrase that isn't in the passage still shows the card (warned, no highlight)");

        log.IsTrue(TtsScriptProcessor.IsRecognisedMarker(DocumentTag), "the TTS processor recognises the tag");
        log.IsTrue(TtsScriptProcessor.IsRecognisedMarker("{Document:\"a\",\"b\",\"c\",4.5,Allegation}"), "…a timed one with a stamp");
        log.IsTrue(TtsScriptProcessor.IsRecognisedMarker("{Document:End}"), "…and its End");
        log.IsTrue(!TtsScriptProcessor.IsRecognisedMarker("{Document:\"only two\",\"fields\",Start}") &&
                   !TtsScriptProcessor.IsRecognisedMarker("{Document:\"bad\",\"keyword\",\"x\",Start,Proven}"),
            "…but not a malformed one (the tag-only check warns about it)");
        log.IsTrue(!HybridAvatarSystem.EmotionTagRegex.IsMatch(DocumentTag) && !HybridAvatarSystem.EmotionTagRegex.IsMatch("{Document:End}"),
            "the emotion parser can't mistake it for an emotion");
        log.IsTrue(!ContentZoneController.IsFeatureCard(ContentCardType.Document), "Document is a side card (hidden at Center)");
    }

    static void Document_StampUnstampRoundTrip(Log log)
    {
        string script =
            "## DOCUMENT\n" +
            "TRM Labs put it plainly. " + DocumentTag + " The attacker still holds it.\n" +
            "That is the report. {Document:End} Next.\n" +
            "{Document:\"The SEC alleges fraud.\",\"alleges fraud\",\"SEC complaint\",6,Allegation} And timed.\n" +
            "{Document:\"No stamp here.\",\"stamp\",\"Src\",Start,Left} Plain. {Document:End}\n";
        foreach (var section in TagGeneration(script, keepCues: false))
        {
            log.IsTrue(section.TimedScript.Contains(",D=0,Report,Right}") && section.TimedScript.Contains(",D=6,Allegation}") &&
                       section.TimedScript.Contains(",D=0,Left}"),
                "stamping gives each Document a T= and D=, keeping its stamp keyword and side");
            var times = new List<float>();
            string raw = TagOnlyRetimer.Unstamp(section.TimedScript, times);
            string original = null;
            foreach (var seg in TtsScriptProcessor.SplitIntoSegments(script))
                if (seg.Name == section.Name) original = seg.Raw;
            log.AreEqual(original, raw, "Unstamp(Stamp(x)) gives back the Document tags as written");

            var (_, events) = ContentZoneTagParser.ParseContentTags(section.TimedScript, 30f);
            log.AreEqual(3, events.FindAll(e => e.cardType == ContentCardType.Document && !e.dismissesCard).Count,
                "the stamped Document tags parse at playback");
            log.AreEqual(2, events.FindAll(e => e.cardType == ContentCardType.Document && e.dismissesCard).Count,
                "…and so do the stamped Ends");
        }
    }

    // -----------------------------------------------------------------------
    // Pure
    // -----------------------------------------------------------------------

    static void Document_StampWords(Log log)
    {
        void Is(DocumentStamp s, string word, string sub)
        {
            string w = DocumentTypography.StampWords(s, out string l);
            log.IsTrue(w == word && l == sub, $"{s} stamps \"{word}\"" + (sub != null ? $" over \"{sub}\"" : "") + $" (got \"{w}\" / \"{l}\")");
        }
        Is(DocumentStamp.Allegation, "ALLEGATION", "NOT PROVEN IN COURT");
        Is(DocumentStamp.Filing, "COURT FILING", null);
        Is(DocumentStamp.Ruling, "RULING", null);
        Is(DocumentStamp.Report, "REPORT", null);
        Is(DocumentStamp.None, null, null);

        Color red = DocumentTypography.StampColor(DocumentStamp.Allegation), blue = DocumentTypography.StampColor(DocumentStamp.Filing);
        Color green = DocumentTypography.StampColor(DocumentStamp.Ruling), grey = DocumentTypography.StampColor(DocumentStamp.Report);
        log.IsTrue(red.r > red.g + 0.4f && red.r > red.b + 0.4f, "Allegation is red");
        log.IsTrue(blue.b > blue.r + 0.3f && blue.b > blue.g + 0.2f, "Filing is blue");
        log.IsTrue(green.g > green.r + 0.2f && green.g > green.b + 0.2f, "Ruling is green");
        log.IsTrue(Mathf.Abs(grey.r - grey.g) < 0.05f && Mathf.Abs(grey.g - grey.b) < 0.05f, "Report is grey");
        log.IsTrue(DocumentTypography.StampLinePx * (DocumentTypography.StampLineFloorPx / DocumentTypography.StampLinePx) >= 32f,
            "the stamp's second line never shrinks under 32 px");
    }

    static void Document_Timeline(Log log)
    {
        log.Near(0.5f, DocumentTypography.LineSweepStart(0), 1e-6f, "the marker starts 0.5 s after the card lands");
        log.Near(0.9f, DocumentTypography.LineSweepStart(1), 1e-6f, "…0.4 s per line, the next line after it");
        log.AreEqual(0f, DocumentTypography.LineSweepAt(0.49f, 0), "nothing before 0.5 s");
        log.Near(0.5f, DocumentTypography.LineSweepAt(0.7f, 0), 1e-5f, "halfway through its line at 0.7 s");
        log.Near(1f, DocumentTypography.LineSweepAt(0.9f, 0), 1e-6f, "the line is done at 0.9 s");
        log.AreEqual(0f, DocumentTypography.LineSweepAt(0.9f, 1), "…as the second line starts");
        log.Near(0.5f + 2 * 0.4f + 0.6f, DocumentTypography.StampStart(2), 1e-5f, "the stamp slams 0.6 s after a 2-line highlight ends");
        log.Near(0.6f, DocumentTypography.StampStart(0), 1e-6f, "…or 0.6 s after landing with no highlight");
        log.Near(DocumentTypography.StampFromScale, DocumentTypography.StampScaleAt(0f), 1e-6f, "the stamp lands at 1.5×");
        log.Near(1f, DocumentTypography.StampScaleAt(DocumentTypography.StampSeconds), 1e-6f, "…and is 1.0 after 0.2 s");
        float lo = 9f;
        for (float t = 0f; t <= DocumentTypography.StampSeconds; t += 0.002f) lo = Mathf.Min(lo, DocumentTypography.StampScaleAt(t));
        log.IsTrue(lo >= 0.95f && lo < 1f, $"…a short punch with a small dip ({lo:F3})");
    }

    static void Document_Geometry(Log log)
    {
        // A 200×100 box at (500,400) rotated −10° (clockwise on screen): its
        // right end drops (larger y in card space), its left end rises.
        Vector2[] p = DocumentTypography.RotatedBox(new Vector2(500f, 400f), new Vector2(200f, 100f), -10f);
        Rect b = DocumentTypography.Bounds(p);
        log.Near(500f, b.center.x, 1e-3f, "a rotated box stays centred (x)");
        log.Near(400f, b.center.y, 1e-3f, "…(y)");
        float h = 100f * Mathf.Cos(10f * Mathf.Deg2Rad) + 200f * Mathf.Sin(10f * Mathf.Deg2Rad);
        log.Near(h, b.height, 1e-3f, "…its bounds grow by the rotation");
        Vector2 topRight = p[1], topLeft = p[0];
        log.IsTrue(topRight.y > topLeft.y, "−10° turns it clockwise: the top edge runs downhill to the right (y down)");

        log.IsTrue(DocumentTypography.Overlaps(p, new Rect(480f, 380f, 40f, 40f)), "a rect inside the box overlaps it");
        log.IsTrue(!DocumentTypography.Overlaps(p, new Rect(700f, 400f, 50f, 50f)), "a rect clear to the right doesn't");
        // Just off a rotated corner: inside the bounds, outside the box.
        Rect nearCorner = new Rect(b.xMax - 6f, b.yMin, 6f, 6f);
        log.IsTrue(!DocumentTypography.Overlaps(p, nearCorner), "a rect in the bounds' empty corner doesn't (true rotated test, not the bounds)");
    }

    // -----------------------------------------------------------------------
    // The real card
    // -----------------------------------------------------------------------

    const string Doc5 = "The keys were never compromised.";
    const string DocTrm = "The attacker still holds about USD 2.4 billion in assets beyond issuer reach.";
    const string Doc40 = "In its complaint the agency alleges that the exchange routed customer deposits through a network of " +
                         "affiliated trading firms, concealed the transfers from its own auditors, and continued to promise users " +
                         "that their funds were held in full and segregated.";
    const string Doc40Phrase = "concealed the transfers from its own auditors";
    const string Doc80 = Doc40 + " The agency further alleges that senior executives were told about the shortfall in writing, " +
                         "approved new withdrawal limits to hide it, and signed quarterly attestations they knew to be false, " +
                         "while the trading firms kept borrowing against customer assets without any collateral.";

    static DocumentCard BuildDocument(string passage, string phrase, string source, DocumentStamp stamp, out GameObject root, bool run = true)
    {
        var card = BuildCard<DocumentCard>(out root);
        card.Initialize(new ContentCardEvent
        {
            cardType = ContentCardType.Document, primaryText = passage, secondaryText = phrase, tertiaryText = source, documentStamp = stamp,
        }, null);
        if (run)
        {
            card.BeginTimelineForTest();
            for (int f = 0; f < 120; f++) card.Advance(1f / 30f); // everything has landed
        }
        return card;
    }

    static int LinesSpanned(TMP_Text t, string text, string phrase)
    {
        int start = text.IndexOf(phrase, StringComparison.Ordinal);
        if (start < 0) return 0;
        t.ForceMeshUpdate(true, true);
        var lines = new HashSet<int>();
        for (int c = start; c < start + phrase.Length && c < t.textInfo.characterCount; c++)
            if (t.textInfo.characterInfo[c].isVisible) lines.Add(t.textInfo.characterInfo[c].lineNumber);
        return lines.Count;
    }

    static void Document_Fonts(Log log)
    {
        log.IsTrue(CardTypography.NotoSerif != null, "Noto Serif Regular loads from Resources/Fonts");
        log.IsTrue(CardTypography.NotoSerifBold != null, "Noto Serif Bold loads from Resources/Fonts");
        log.IsTrue(System.IO.File.Exists("Assets/Resources/Fonts/NotoSerif-OFL.txt"), "…with its OFL licence next to it");
    }

    static void CheckDocument(Log log, DocumentCard card, string passage, string phrase, DocumentStamp stamp, string what)
    {
        var rt = (RectTransform)card.transform;
        CardTypography.Report report = card.Layout;
        Vector2 size = CardTypography.CardSize(rt);
        float pad = CardTypography.PaddingFor(size);
        bool overflowWarned = report.Warnings.Exists(w => w.Contains("doesn't fit"));

        foreach (CardTypography.Element e in report.Elements)
            log.IsTrue(!e.BelowFloor || report.WarnedAbout(e.Name),
                $"{what}: {e.Name} {e.Size:F0} px is at/above its {e.Floor:F0} px floor or logged");
        log.IsTrue(card.BodySize <= DocumentTypography.BodyMaxPx + 0.01f, $"{what}: passage at most 52 px ({card.BodySize:F0})");
        log.IsTrue(card.Passage.font == CardTypography.NotoSerif, $"{what}: the passage is in Noto Serif");
        log.IsTrue(card.Header.font == CardTypography.NotoSerifBold && (card.Header.fontStyle & FontStyles.SmallCaps) != 0,
            $"{what}: the source is in Noto Serif Bold small caps");

        foreach (TMP_Text t in card.GetComponentsInChildren<TMP_Text>(false))
        {
            if (string.IsNullOrEmpty(t.text)) continue;
            log.IsTrue(t.fontSize >= CardTypography.HardFloorPx - 0.01f, $"{what}: {t.name} at or above 32 px ({t.fontSize:F1})");
            log.IsTrue(!t.isTextTruncated, $"{what}: {t.name} not truncated");
        }

        Rect body = Rect.MinMaxRect(pad, card.StripBottom, size.x - pad, size.y - pad);
        Rect passageInk = CardTypography.InkInCard(card.Passage, rt);
        log.IsTrue(overflowWarned || Inside(passageInk, body, 1f), $"{what}: the passage is inside the padding, under the strip ({Fmt(passageInk)} in {Fmt(body)})");
        if (card.Header.text.Length > 0)
        {
            Rect strip = Rect.MinMaxRect(pad, 8f, size.x - pad, card.StripBottom);
            Rect headerInk = CardTypography.InkInCard(card.Header, rt);
            log.IsTrue(Inside(headerInk, strip, 1f), $"{what}: the source sits in the header strip ({Fmt(headerInk)} in {Fmt(strip)})");
        }

        // The marker: one stroke per line the phrase spans, each under the phrase's ink, all behind the passage.
        int spans = phrase.Length > 0 ? LinesSpanned(card.Passage, passage, phrase) : 0;
        log.AreEqual(spans, card.MarkerCount, $"{what}: one marker stroke per line of the phrase ({spans})");
        log.IsTrue(card.MarkersRoot.GetSiblingIndex() < card.Passage.transform.GetSiblingIndex(), $"{what}: the marker is behind the passage");
        for (int i = 0; i < card.MarkerCount; i++)
        {
            Rect m = RectInCard(card.Marker(i).rectTransform, rt);
            Rect want = card.HighlightRects[i];
            log.Near(want.width, m.width, 0.5f, $"{what}: marker {i} has swept its whole line");
            log.IsTrue(card.Marker(i).enabled && card.Marker(i).color.a > 0.5f, $"{what}: marker {i} is visible");
        }
        int start = passage.IndexOf(phrase, StringComparison.Ordinal);
        if (spans > 0)
        {
            TMP_TextInfo info = card.Passage.textInfo;
            for (int c = start; c < start + phrase.Length; c++)
            {
                TMP_CharacterInfo ci = info.characterInfo[c];
                if (!ci.isVisible) continue;
                Vector3 mid = rt.InverseTransformPoint(card.Passage.rectTransform.TransformPoint((ci.bottomLeft + ci.topRight) * 0.5f));
                var p = new Vector2(mid.x - rt.rect.xMin, rt.rect.yMax - mid.y);
                bool covered = false;
                foreach (Rect h in card.HighlightRects) if (h.Contains(p)) { covered = true; break; }
                if (!covered) { log.IsTrue(false, $"{what}: \"{passage[c]}\" at {c} is under the marker"); break; }
            }
        }

        // The stamp: its fixed words, rotated −10°, inside the padding, never over the highlight.
        if (stamp == DocumentStamp.None)
        {
            log.IsTrue(!card.StampPlaced && !card.Stamp.gameObject.activeSelf, $"{what}: no keyword, no stamp");
            return;
        }
        bool skipWarned = report.Warnings.Exists(w => w.Contains("no place for the"));
        log.IsTrue(card.StampPlaced || skipWarned, $"{what}: the {stamp} stamp is placed (or its absence is warned)");
        if (!card.StampPlaced) return;

        string word = DocumentTypography.StampWords(stamp, out string sub);
        log.IsTrue(card.StampWord.text == word && (sub == null ? !card.StampLine.gameObject.activeSelf : card.StampLine.text == sub),
            $"{what}: the stamp reads \"{word}\"" + (sub != null ? $" / \"{sub}\"" : ""));
        log.Near(1f, card.Stamp.localScale.x, 1e-4f, $"{what}: the stamp has settled to 1.0×");
        log.Near(card.StampInk, card.StampAlpha, 1e-4f, $"{what}: the stamp is inked");
        float z = card.Stamp.localEulerAngles.z; if (z > 180f) z -= 360f;
        log.Near(DocumentTypography.StampRotationDegrees, z, 1e-3f, $"{what}: the stamp is rotated −10°");

        // The polygon the placement used is where the stamp really is.
        var corners = new Vector3[4];
        card.Stamp.GetWorldCorners(corners);
        float worst = 0f;
        foreach (Vector3 w in corners)
        {
            Vector3 l = rt.InverseTransformPoint(w);
            var q = new Vector2(l.x - rt.rect.xMin, rt.rect.yMax - l.y);
            float d = float.MaxValue;
            foreach (Vector2 pp in card.StampPolygon) d = Mathf.Min(d, Vector2.Distance(pp, q));
            worst = Mathf.Max(worst, d);
        }
        log.IsTrue(worst < 0.5f, $"{what}: the stamp's drawn corners match its placement polygon (off by {worst:F2} px)");

        Rect inner = Rect.MinMaxRect(pad, card.StripBottom, size.x - pad, size.y - pad);
        foreach (Vector2 c in card.StampPolygon)
            if (!inner.Contains(c) && !(Mathf.Abs(c.x - inner.xMax) < 0.5f || Mathf.Abs(c.y - inner.yMax) < 0.5f))
            { log.IsTrue(false, $"{what}: stamp corner {c} is inside the padding"); break; }
        foreach (Rect h in card.HighlightRects)
            log.IsTrue(!DocumentTypography.Overlaps(card.StampPolygon, h), $"{what}: the stamp never covers the highlight ({Fmt(h)})");
        bool coversText = false;
        foreach (Rect ink in card.LineInkRects)
            if (DocumentTypography.Overlaps(card.StampPolygon, ink)) coversText = true;
        if (card.StampInBand)
            log.IsTrue(!coversText, $"{what}: in its band the stamp covers no passage text");
        log.AreEqual(coversText, card.StampBehindText, $"{what}: a stamp over passage text is printed under it (and only then)");
        if (coversText)
        {
            log.IsTrue(card.Stamp.GetSiblingIndex() < card.Passage.transform.GetSiblingIndex() &&
                       card.Stamp.GetSiblingIndex() > card.MarkersRoot.GetSiblingIndex(),
                $"{what}: …between the marker and the words, so every word stays on top");
            log.Near(DocumentTypography.StampUnderTextAlpha, card.StampAlpha, 1e-4f, $"{what}: …in lighter ink");
            log.IsTrue(report.Warnings.Exists(w => w.Contains("printed under")), $"{what}: …and it says so");
        }
        else log.IsTrue(card.Stamp.GetSiblingIndex() > card.Passage.transform.GetSiblingIndex(), $"{what}: the stamp is on top of the page");

        // Its words inside its own frame.
        var srt = card.Stamp;
        float frame = DocumentTypography.StampFramePx * CardTypography.ScaleFor(size);
        Rect frameInner = Rect.MinMaxRect(frame, frame, srt.rect.width - frame, srt.rect.height - frame);
        foreach (TMP_Text t in new TMP_Text[] { card.StampWord, card.StampLine })
        {
            if (!t.gameObject.activeSelf) continue;
            Rect ink = CardTypography.InkInCard(t, srt);
            log.IsTrue(Inside(ink, frameInner, 1f), $"{what}: \"{t.text}\" inside the stamp's frame ({Fmt(ink)} in {Fmt(frameInner)})");
        }

        Debug.Log($"[Document] self-test {what}: passage {card.BodySize:F0} px, {spans} marker line(s), stamp {stamp} " +
                  $"{(card.StampInBand ? "in its band" : coversText ? "under part of the passage" : "beside the passage")} at {card.StampFitScale:F2}× " +
                  $"({Fmt(DocumentTypography.Bounds(card.StampPolygon))})" +
                  (report.Warnings.Count > 0 ? $"; warnings: {string.Join(" | ", report.Warnings)}" : ""));
    }

    static void Document_CardLayout(Log log)
    {
        var cases = new List<(string p, string h, string s, DocumentStamp st, string what)>
        {
            (Doc5, "never compromised", "Exchange statement", DocumentStamp.Report, "5 words, Report"),
            (DocTrm, "beyond issuer reach", "TRM Labs", DocumentStamp.Report, "TRM, Report"),
            (Doc5, "never compromised", "", DocumentStamp.None, "5 words, no stamp, no source"),
        };
        foreach (DocumentStamp st in new[] { DocumentStamp.Allegation, DocumentStamp.Filing, DocumentStamp.Ruling, DocumentStamp.Report })
            cases.Add((Doc40, Doc40Phrase, "SEC v. Exchange, complaint, ¶ 41", st, $"40 words, {st}"));
        cases.Add((Doc80, Doc40Phrase, "SEC v. Exchange", DocumentStamp.Allegation, "80 words, Allegation"));
        cases.Add((Doc80, "without any collateral", "SEC v. Exchange", DocumentStamp.Allegation, "80 words, phrase at the end"));

        foreach (var c in cases)
        {
            var card = BuildDocument(c.p, c.h, c.s, c.st, out GameObject root);
            try
            {
                CheckDocument(log, card, c.p, c.h, c.st, $"Document ({c.what})");
                if (c.what.StartsWith("40 words"))
                    log.IsTrue(card.MarkerCount >= 2, $"Document ({c.what}): the phrase spans a line break ({card.MarkerCount} strokes)");
                if (c.what == "5 words, Report" || c.what == "TRM, Report")
                    log.IsTrue(card.StampInBand && card.BodySize >= DocumentTypography.BodyMaxPx - 0.01f,
                        $"Document ({c.what}): a short passage keeps 52 px with the stamp in its own band");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    static void Document_PhraseNotFound(Log log)
    {
        var card = BuildDocument(DocTrm, "beyond the issuer's reach", "TRM Labs", DocumentStamp.Report, out GameObject root, run: false);
        try
        {
            log.AreEqual(0, card.MarkerCount, "a phrase that isn't in the passage gets no highlight");
            card.BeginTimelineForTest();
            const float dt = 1f / 30f;
            float shownAt = float.NaN, t = 0f;
            for (int f = 0; f < 60; f++)
            {
                card.Advance(dt); t += dt;
                if (float.IsNaN(shownAt) && card.StampAlpha > 0f) shownAt = t;
            }
            log.IsTrue(shownAt >= 0.6f - 1e-3f && shownAt <= 0.6f + dt + 1e-3f,
                $"…and the stamp slams 0.6 s after landing ({shownAt:F3} s)");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // Steps a 2+-line highlight and its stamp at 30 fps.
    static void Document_TimelineFrameByFrame(Log log)
    {
        var card = BuildDocument(Doc40, Doc40Phrase, "SEC v. Exchange", DocumentStamp.Allegation, out GameObject root, run: false);
        try
        {
            var rt = (RectTransform)card.transform;
            int lines = card.MarkerCount;
            log.IsTrue(lines >= 2 && !card.Marker(0).enabled && card.StampAlpha == 0f, "before landing: no marker, no stamp");
            card.BeginTimelineForTest();
            const float dt = 1f / 30f;
            float t = 0f;
            var lineStart = new float[lines];
            var lineDone = new float[lines];
            for (int i = 0; i < lines; i++) { lineStart[i] = float.NaN; lineDone[i] = float.NaN; }
            float stampAt = float.NaN, firstScale = 0f, settledAt = float.NaN;
            bool growsLeftToRight = true;
            for (int f = 0; f < 120; f++)
            {
                card.Advance(dt); t += dt;
                for (int i = 0; i < lines; i++)
                {
                    Rect m = RectInCard(card.Marker(i).rectTransform, rt);
                    if (card.Marker(i).enabled && float.IsNaN(lineStart[i])) lineStart[i] = t;
                    if (card.Marker(i).enabled && Mathf.Abs(m.xMin - card.HighlightRects[i].xMin) > 0.5f) growsLeftToRight = false;
                    if (float.IsNaN(lineDone[i]) && m.width >= card.HighlightRects[i].width - 0.5f && card.Marker(i).enabled) lineDone[i] = t;
                }
                if (card.StampAlpha > 0f)
                {
                    if (float.IsNaN(stampAt)) { stampAt = t; firstScale = card.Stamp.localScale.x; }
                    if (float.IsNaN(settledAt) && Mathf.Abs(card.Stamp.localScale.x - 1f) < 1e-4f) settledAt = t;
                }
            }
            for (int i = 0; i < lines; i++)
            {
                float s = DocumentTypography.LineSweepStart(i);
                log.IsTrue(lineStart[i] > s - 1e-3f && lineStart[i] <= s + dt + 1e-3f, $"marker line {i} starts at {s:F1} s ({lineStart[i]:F3})");
                log.IsTrue(lineDone[i] >= s + DocumentTypography.HighlightLineSeconds - dt - 1e-3f &&
                           lineDone[i] <= s + DocumentTypography.HighlightLineSeconds + dt + 1e-3f,
                    $"…and has swept its line 0.4 s later ({lineDone[i]:F3})");
            }
            log.IsTrue(growsLeftToRight, "every stroke grows from the phrase's left end");
            float want = DocumentTypography.StampStart(lines);
            log.IsTrue(stampAt >= want - 1e-3f && stampAt <= want + dt + 1e-3f, $"the stamp slams 0.6 s after the highlight ({stampAt:F3} s, due {want:F2})");
            log.Near(DocumentTypography.StampFromScale, firstScale, 1e-4f, "…its first drawn frame at the full 1.5×");
            log.IsTrue(settledAt - stampAt <= DocumentTypography.StampSeconds + dt + 1e-3f, $"…settled within 0.2 s ({settledAt - stampAt:F3})");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
