using System;
using System.Collections.Generic;
using UnityEngine;

namespace HS.Rapport
{
    /// <summary>One Moment the Opportunity Director put in front of the player (GDD §4.4).</summary>
    public sealed class MomentOffer
    {
        public string Id;
        public float Weight;
        public int Chapter;
        public int Room = -1;
        public float OpenedAt;
        public float ClosesAt = float.PositiveInfinity;
        public object Subject;            // what the moment is about (an enemy, a duel, a penalty...)
        public string Note;
        public bool Captured { get; internal set; }
        public bool Closed { get; internal set; }
        public bool Open => !Captured && !Closed;
    }

    public sealed class PenaltyEntry
    {
        public string Id;
        public float Amount;
        public int Chapter;
        public int Room = -1;
        public float Time;
        public string Note;
        public bool Refunded { get; internal set; }
    }

    /// <summary>
    /// The hidden stat (GDD §4.4). Capture rate = (earned − penalties) / offered so far; penalties are capped per
    /// chapter at 30% of that chapter's offered points; uncaptured Moments still count as offered; neglect earns zero,
    /// not a negative. Pure C# — deterministic, snapshot-able for Restore Points, and readable by the Post-Mortem.
    /// </summary>
    public sealed class RapportLedger
    {
        public const int Chapters = 5;
        public const float PenaltyCapFraction = 0.30f;
        static readonly float[] Budgets = { 60f, 90f, 100f, 110f, 40f };

        public struct LogEntry
        {
            public string Kind;   // offer | capture | close | penalty | refund
            public string Id;
            public float Points;
            public int Chapter;
            public int Room;
            public float Time;
            public string Note;
        }

        public int Chapter = 1;
        public int Room = -1;
        public Func<float> Clock = () => 0f;
        public readonly List<LogEntry> Log = new List<LogEntry>();
        public event Action<LogEntry> Changed;

        float[] _offered = new float[Chapters + 1];
        float[] _earned = new float[Chapters + 1];
        float[] _penalty = new float[Chapters + 1];
        readonly List<PenaltyEntry> _penalties = new List<PenaltyEntry>();

        public float Budget(int chapter) => Budgets[Mathf.Clamp(chapter, 1, Chapters) - 1];
        int Ch => Mathf.Clamp(Chapter, 1, Chapters);

        public float Offered => Sum(_offered);
        public float Earned => Sum(_earned);
        public float OfferedIn(int ch) => _offered[Mathf.Clamp(ch, 1, Chapters)];
        public float EarnedIn(int ch) => _earned[Mathf.Clamp(ch, 1, Chapters)];
        public float RawPenaltiesIn(int ch) => _penalty[Mathf.Clamp(ch, 1, Chapters)];
        public float EffectivePenaltiesIn(int ch) => Mathf.Min(RawPenaltiesIn(ch), PenaltyCapFraction * OfferedIn(ch));

        public float EffectivePenalties
        {
            get
            {
                float p = 0f;
                for (int c = 1; c <= Chapters; c++) p += EffectivePenaltiesIn(c);
                return p;
            }
        }

        public float CaptureRate
        {
            get
            {
                float offered = Offered;
                if (offered <= 0f) return 0f;
                return Mathf.Clamp01((Earned - EffectivePenalties) / offered);
            }
        }

        /// <summary>Put a Moment in front of the player. Returns null when the chapter's offer budget is spent.</summary>
        public MomentOffer Offer(string id, float weight, object subject = null, float window = float.PositiveInfinity, string note = null)
        {
            if (weight <= 0f) return null;
            if (_offered[Ch] + weight > Budget(Ch) + 1e-4f) return null;
            float now = Clock();
            var m = new MomentOffer
            {
                Id = id, Weight = weight, Chapter = Ch, Room = Room, OpenedAt = now, ClosesAt = now + window, Subject = subject, Note = note,
            };
            _offered[Ch] += weight;
            Add("offer", id, weight, note);
            return m;
        }

        public bool Capture(MomentOffer m, string note = null)
        {
            if (m == null || !m.Open) return false;
            if (Clock() > m.ClosesAt + 1e-4f)
            {
                Close(m, "expired");
                return false;
            }
            m.Captured = true;
            _earned[Mathf.Clamp(m.Chapter, 1, Chapters)] += m.Weight;
            Add("capture", m.Id, m.Weight, Join(m.Note, note));
            return true;
        }

        /// <summary>
        /// Take back an offer that became impossible to capture through no fault of the player (e.g. the cheater became
        /// the hero's own duel opponent — "valid measurement", GDD §11.3). Only open, uncaptured offers.
        /// </summary>
        public bool Withdraw(MomentOffer m, string note = null)
        {
            if (m == null || !m.Open) return false;
            m.Closed = true;
            _offered[Mathf.Clamp(m.Chapter, 1, Chapters)] -= m.Weight;
            Add("withdraw", m.Id, -m.Weight, Join(m.Note, note));
            return true;
        }

        /// <summary>The window passed uncaptured (still counts as offered).</summary>
        public void Close(MomentOffer m, string note = null)
        {
            if (m == null || !m.Open) return;
            m.Closed = true;
            Add("close", m.Id, 0f, Join(m.Note, note));
        }

        /// <summary>Log notes keep the Moment's subject and append what happened: "subject | outcome".</summary>
        static string Join(string subject, string outcome) =>
            string.IsNullOrEmpty(outcome) ? subject : string.IsNullOrEmpty(subject) ? outcome : subject + " | " + outcome;

        public PenaltyEntry Penalize(string id, float amount, string note = null)
        {
            if (amount <= 0f) return null;
            var p = new PenaltyEntry { Id = id, Amount = amount, Chapter = Ch, Room = Room, Time = Clock(), Note = note };
            _penalties.Add(p);
            _penalty[Ch] += amount;
            Add("penalty", id, -amount, note);
            return p;
        }

        public bool Refund(PenaltyEntry p, string note = null)
        {
            if (p == null || p.Refunded) return false;
            p.Refunded = true;
            _penalty[Mathf.Clamp(p.Chapter, 1, Chapters)] -= p.Amount;
            Add("refund", p.Id, p.Amount, note);
            return true;
        }

        void Add(string kind, string id, float points, string note)
        {
            var e = new LogEntry { Kind = kind, Id = id, Points = points, Chapter = Ch, Room = Room, Time = Clock(), Note = note };
            Log.Add(e);
            Changed?.Invoke(e);
        }

        static float Sum(float[] a)
        {
            float s = 0f;
            for (int i = 1; i < a.Length; i++) s += a[i];
            return s;
        }

        // ------------------------------------------------------------------ restore points

        public sealed class State
        {
            internal float[] Offered, Earned, Penalty;
            internal int LogCount, Chapter;
        }

        public State Snapshot() => new State
        {
            Offered = (float[])_offered.Clone(), Earned = (float[])_earned.Clone(), Penalty = (float[])_penalty.Clone(),
            LogCount = Log.Count, Chapter = Chapter,
        };

        public void Restore(State s)
        {
            _offered = (float[])s.Offered.Clone();
            _earned = (float[])s.Earned.Clone();
            _penalty = (float[])s.Penalty.Clone();
            if (Log.Count > s.LogCount) Log.RemoveRange(s.LogCount, Log.Count - s.LogCount);
            Chapter = s.Chapter;
            _penalties.Clear();
        }
    }
}
