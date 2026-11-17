using System.Collections.Generic;
using UnityEngine;

namespace MugsTech.Stage
{
    /// <summary>One {Position:…} of the take, in order of time (those on a {Transition} line included).</summary>
    public struct PositionCue
    {
        /// <summary>Its final time on the voice clock — after every timing correction; this is never changed.</summary>
        public float Time;
        public CharacterPosition Position;
        /// <summary>It is a cut (",Cut", or the scene's hard cuts).</summary>
        public bool Cut;
        /// <summary>It sits on a {Transition:…} line: applied as a snap under the transition's cover.</summary>
        public bool OnTransition;
    }

    /// <summary>When a position's move starts, relative to its tag.</summary>
    public struct ArrivalLead
    {
        /// <summary>True when the move starts before the tag, so he has landed on its word.</summary>
        public bool Led;
        /// <summary>When the move starts (the tag's own time when not led).</summary>
        public float Start;
        /// <summary>How long before the tag it starts.</summary>
        public float Seconds;
        /// <summary>Led, but by less than the move takes: he lands late by the difference.</summary>
        public bool Shortened;
        /// <summary>Why it is not led (null when it is).</summary>
        public string Why;
    }

    /// <summary>
    /// "Arrive on the word": an arrival from off stage — a pop-up at a full
    /// position, a peek's rise, a lean-in — starts early by its own duration,
    /// so he is standing there on the first word of his line instead of
    /// rising through it.
    ///
    /// Pure, and it only READS the tags' times: by the time the recording
    /// scene parses them they are final (forced alignment, the audio-measured
    /// rescale, the pause snap, the {Pause} pins, the same-beat join are all
    /// upstream). Nothing here moves a tag; every other tag on the beat still
    /// fires on its time, and so does the stage's own change (the wide card
    /// gliding to its side, the full-frame picture ending). Only his travel
    /// starts early.
    /// </summary>
    public static class ArrivalLeads
    {
        /// <summary>
        /// A position on a {Transition} line lands at the cover's midpoint,
        /// about a third of a second after the transition's time: he isn't
        /// off (or back) before then.
        /// </summary>
        public const float TransitionCoverSeconds = 0.4f;

        /// <summary>
        /// The lead of every cue. <paramref name="cues"/> in order of time;
        /// <paramref name="blacks"/> the {Black} stretches as (from, to).
        /// Not led: his leaving (he leaves on the tag), a cut, a move between
        /// Left / Right / Center or out of a peek or a lean (he is on screen:
        /// moving on the word is right), an arrival on a {Transition} line or
        /// under a {Black}, and the script's first position. A lead never
        /// starts before the take does, nor before his previous move — the
        /// one that took him off — has ended.
        /// </summary>
        public static ArrivalLead[] Plan(IList<PositionCue> cues, IList<Vector2> blacks)
        {
            var leads = new ArrivalLead[cues != null ? cues.Count : 0];
            CharacterPosition at = CharacterPosition.Center, before = CharacterPosition.Center;
            for (int i = 0; i < leads.Length; i++)
            {
                PositionCue cue = cues[i];
                var lead = new ArrivalLead { Start = cue.Time };

                if (i == 0) lead.Why = "the script's first position";
                else if (cue.Position == CharacterPosition.Off) lead.Why = "he leaves on the tag";
                else if (cue.Position == at) lead.Why = "he is already there";
                else if (at != CharacterPosition.Off) lead.Why = "he is on screen, so he moves on the word";
                else if (cue.OnTransition) lead.Why = "on a {Transition} line";
                else if (cue.Cut) lead.Why = "a cut";
                else if (UnderBlack(cue.Time, blacks)) lead.Why = "under a {Black}";
                else
                {
                    // When the move that took him off has ended.
                    PositionCue left = cues[i - 1];
                    float earliest = i - 1 == 0 && !left.OnTransition ? 0f   // an opening {Position:Off}: he is off from the first frame
                                   : left.OnTransition ? left.Time + TransitionCoverSeconds
                                   : left.Cut ? left.Time
                                   : left.Time + StageMath.TravelSeconds(before, CharacterPosition.Off);
                    float full = StageMath.TravelSeconds(CharacterPosition.Off, cue.Position);
                    float start = Mathf.Max(cue.Time - full, Mathf.Max(0f, earliest));
                    if (start < cue.Time - 1e-4f)
                    {
                        lead.Led = true;
                        lead.Start = start;
                        lead.Seconds = cue.Time - start;
                        lead.Shortened = lead.Seconds < full - 1e-3f;
                    }
                    else lead.Why = "no room: his move out ends on this tag";
                }

                leads[i] = lead;
                before = at;
                at = cue.Position;
            }
            return leads;
        }

        /// <summary>True when a {Black} is still up at <paramref name="time"/> (one that ends on it is not).</summary>
        public static bool UnderBlack(float time, IList<Vector2> blacks)
        {
            if (blacks == null) return false;
            foreach (Vector2 b in blacks)
                if (time >= b.x - 1e-3f && time < b.y - 1e-3f) return true;
            return false;
        }

        /// <summary>
        /// The {Black} stretches of a take as (from, to), from its markers:
        /// (time, seconds, held, isEnd). A held one lasts to the next End (or
        /// to <paramref name="takeEnd"/>).
        /// </summary>
        public static List<Vector2> BlackStretches(IList<(float time, float seconds, bool held, bool isEnd)> markers, float takeEnd)
        {
            var stretches = new List<Vector2>();
            if (markers == null) return stretches;
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m.isEnd) continue;
                if (!m.held) { stretches.Add(new Vector2(m.time, m.time + Mathf.Max(0f, m.seconds))); continue; }
                float to = takeEnd;
                for (int j = i + 1; j < markers.Count; j++)
                    if (markers[j].isEnd && markers[j].time >= m.time) { to = markers[j].time; break; }
                stretches.Add(new Vector2(m.time, to));
            }
            return stretches;
        }
    }
}
