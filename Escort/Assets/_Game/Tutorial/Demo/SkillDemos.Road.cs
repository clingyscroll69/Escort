using HS.Presentation;
using UnityEngine;

namespace HS.Tutorial.Demo
{
    /// <summary>The road's tricks (Whisperwood and the Catacombs): Splint &amp; Stitch, Pull Back, Sling, Read the Room,
    /// Read Runes, Lockpick, Map Sketch, Buckler. Draft captions for the owner to rewrite.</summary>
    public static partial class SkillDemos
    {
        static readonly Color Stone = new Color(0.42f, 0.4f, 0.46f, 1f);
        static readonly Color Rune = new Color(0.62f, 0.45f, 1f, 1f);
        static readonly Color Iron = new Color(0.3f, 0.31f, 0.33f, 1f);
        static readonly Color Chalk = new Color(1f, 0.9f, 0.62f, 0.8f);

        // =================================================================================== Splint & Stitch
        static DemoScript SplintAndStitch(DemoContext c)
        {
            float channel = c.Def != null ? c.Def.A(c.Rank) : 6f, supplies = c.Def != null ? c.Def.B(c.Rank) : 2f;
            var s = new DemoScript();
            var callum = c.Spawn("callum", "callum", new Vector2(0.4f, 0.8f), 200f);
            c.Spawn("sk", "sidekick", new Vector2(-4.4f, -2.2f), 60f);
            s.At(0f, cx =>
            {
                cx.Overlay.Bar("hp", callum, 0.55f, HpRed);
                cx.Overlay.Mark(callum, "wound_ribs", 3f + channel, new Color(1f, 0.42f, 0.36f), 12f);
            });
            s.Caption(0.1f, "Cracked ribs: a serious wound. A bandage won't touch it; a splint will.");
            s.Walk("sk", 0.3f, 1.7f, new Vector2(-4.4f, -2.2f), new Vector2(-0.9f, 0.1f));
            float done = 1.8f + channel;
            s.At(1.8f, cx =>
            {
                cx["sk"].Face(callum.Pos);
                cx["sk"].Play("bandage", channel);
                cx.Overlay.SkillCallout(cx["sk"], "splint_and_stitch", 1.4f);
                DemoContext.Sound("cloth", 0.45f);
            });
            s.At(2.6f, cx => cx.Overlay.Bubble(callum, "Careful. Careful! ...Fine.", 2.4f));
            s.Caption(1.9f, $"A {N(channel)} s channel right beside him. Stay still; he grumbles through it.");
            s.Over(1.8f, done - 0.02f, (cx, u) => cx.Overlay.Bar("channel", cx["sk"], u, Cyan));
            s.At(done, cx =>
            {
                cx.Overlay.RemoveBar("channel");
                cx["sk"].Play("none");
                cx.Overlay.ClearMarks(callum);
                cx.Overlay.Mark(callum, "check", 1.8f, new Color(0.55f, 1f, 0.6f), 12f);
                Vfx.Burst(VfxKind.Heal, callum.Root.position + Vector3.up * 0.8f, 1f);
                DemoContext.Sound("ui_confirm", 0.35f);
            });
            s.Caption(done + 0.1f, $"The wound is treated. Supplies for {N(supplies)} a chapter, so choose which.");
            return s.End(done + 3.2f);
        }

        // ======================================================================================== Pull Back
        static DemoScript PullBack(DemoContext c)
        {
            float reach = c.Def != null ? c.Def.A(c.Rank) : 10f, pull = c.Def != null ? c.Def.B(c.Rank) : 4f;
            var s = new DemoScript();
            var from = new Vector2(1.8f, 1.0f);
            var skAt = new Vector2(-4.2f, -1.6f);
            float dist = Vector2.Distance(from, skAt);
            var to = from + (skAt - from).normalized * Mathf.Min(pull, dist - 1.3f);
            var callum = c.Spawn("callum", "callum", from, 90f);
            c.Spawn("thug", "thug", new Vector2(6.2f, 1.6f), -90f);
            c.Spawn("sk", "sidekick", skAt, 60f);
            s.At(0f, cx =>
            {
                cx.Ring("snare", from, 0.7f, Red);
                cx.Overlay.Mark(callum, "snare", 2.2f, new Color(1f, 0.5f, 0.4f));
                callum.Play("hit");
            });
            s.Caption(0.1f, "A snare has him by the ankle, and a bandit is coming for him.");
            s.Walk("thug", 0.4f, 2.8f, new Vector2(6.2f, 1.6f), new Vector2(2.9f, 1.1f));
            s.Caption(1.2f, $"From as far as {N(reach)} m, haul him toward you:");
            s.At(1.9f, cx =>
            {
                cx["sk"].Face(callum.Pos);
                cx["sk"].Play("throw", 0.6f);
                cx.Overlay.SkillCallout(cx["sk"], "pull_back", 1.4f);
                cx.Line("rope", Chest(cx["sk"]), Chest(callum), new Color(0.85f, 0.72f, 0.5f, 0.9f), 0.04f);
                DemoContext.Sound("syn_whoosh", 0.4f);
            });
            s.Over(2.1f, 2.5f, (cx, u) =>
            {
                callum.Pos = Vector2.Lerp(from, to, u);
                var rope = cx.Decal("rope");
                if (rope != null) rope.GetComponent<LineRenderer>().SetPosition(1, Chest(callum));
            });
            s.At(2.5f, cx =>
            {
                cx.RemoveDecal("rope");
                cx.RemoveDecal("snare");
                Vfx.Burst(VfxKind.Dust, cx.Stage.World(from) + Vector3.up * 0.2f, 0.9f);
                cx.Overlay.ClearMarks(callum);
            });
            s.Caption(2.6f, $"Up to {N(pull)} m, out of the snare and out of the blow.");
            s.At(2.9f, cx =>
            {
                cx["thug"].Face(from);
                cx["thug"].Play("attack", 0.6f);
            });
            s.At(3.5f, cx => cx.Overlay.Bubble(callum, "Unhand— ...oh. Thank you.", 2.6f));
            s.Caption(4.4f, "Undignified, but no dishonour in a rope. He may even thank you.");
            return s.End(7.6f);
        }

        // ============================================================================================ Sling
        static DemoScript Sling(DemoContext c)
        {
            float dmg = c.Def != null ? c.Def.A(c.Rank) : 9f, range = c.Def != null ? c.Def.B(c.Rank) : 16f, cd = c.Def != null ? c.Def.Cooldown(c.Rank) : 1.4f;
            const float shooterHp = 50f;
            var s = new DemoScript();
            c.Prop("perch", "crate_perch", new Vector2(3.6f, 2.6f), 0f);
            var xbow = c.Spawn("xbow", "crossbowman", new Vector2(3.6f, 2.6f), 225f);
            if (xbow != null)
            {
                xbow.Height = 0.95f;
                xbow.Pos = xbow.Pos;
            }
            var callum = c.Spawn("callum", "callum", new Vector2(-2.4f, 0.6f), 90f);
            c.Spawn("thug", "thug", new Vector2(-0.6f, 0.6f), -90f);
            c.Spawn("sk", "sidekick", new Vector2(-3.8f, -2.4f), 45f);
            float hp = 1f;
            s.At(0f, cx =>
            {
                callum.Flag("combat", true);
                cx["thug"].Flag("combat", true);
                cx.Cone("cone", callum, 120f, 7f);
                cx["xbow"].Play("aim");
                cx.Line("aim", Chest(cx["xbow"]), Chest(callum), Red, 0.04f);
                cx.Overlay.Bar("hp", cx["xbow"], 1f, HpRed);
            });
            Duel(s, "callum", "thug", 0.3f, 9.6f);
            s.Caption(0.1f, $"A stone, out to {N(range)} m. It never runs out.");
            for (int k = 0; k < 3; k++)
            {
                float t = 0.9f + k * Mathf.Max(cd, 1.1f);
                s.At(t, cx =>
                {
                    cx["sk"].Face(cx["xbow"].Pos);
                    cx["sk"].Play("throw", 0.5f);
                    if (t < 1f) cx.Overlay.SkillCallout(cx["sk"], "sling", 1.4f);
                });
                Throw(s, "stone" + k, "sk", cx => Chest(cx["xbow"]), t + 0.15f, t + 0.45f);
                s.At(t + 0.45f, cx =>
                {
                    cx["xbow"].Play("hit", 0.4f);
                    cx.RemoveDecal("aim");
                    hp = Mathf.Max(0.1f, hp - dmg / shooterHp);
                    cx.Overlay.Bar("hp", cx["xbow"], hp, HpRed);
                    DemoContext.Sound("thud", 0.35f);
                });
                s.At(t + 0.9f, cx =>
                {
                    cx["xbow"].Play("aim");
                    cx.Line("aim", Chest(cx["xbow"]), Chest(callum), Red, 0.04f);
                });
            }
            s.Caption(1.5f, $"{N(dmg)} damage and a flinch that spoils his aim. Again in {N(cd)} s.");
            float duel = 0.9f + 3f * Mathf.Max(cd, 1.1f) + 0.6f;
            s.Caption(duel - 0.4f, "A stone into his duel is a slight, if he sees it:");
            s.At(duel, cx =>
            {
                cx["sk"].Face(cx["thug"].Pos);
                cx["sk"].Play("throw", 0.5f);
            });
            Throw(s, "stone_duel", "sk", cx => Chest(cx["thug"]), duel + 0.15f, duel + 0.4f);
            s.At(duel + 0.4f, cx =>
            {
                cx["thug"].Play("hit", 0.4f);
                cx.Overlay.Bubble(callum, "Let a man finish his own fight.", 2.6f);
            });
            s.Over(duel + 0.4f, duel + 0.9f, (cx, u) => cx.Overlay.Bar("honor", callum, Mathf.Lerp(1f, 0.92f, u), HonorGold, 34f));
            return s.End(duel + 3.2f);
        }

        // ===================================================================================== Read the Room
        static DemoScript ReadTheRoom(DemoContext c)
        {
            float lasts = c.Def != null ? c.Def.A(c.Rank) : 6f, radius = c.Def != null ? c.Def.B(c.Rank) : 20f;
            var s = new DemoScript();
            c.Spawn("callum", "callum", new Vector2(-4.6f, 1.0f), 90f);
            c.Spawn("brute", "brute", new Vector2(0.6f, 2.6f), 200f);
            c.Spawn("turncoat", "turncoat", new Vector2(-1.6f, 3.2f), 160f);
            c.Prop("bush", "bush", new Vector2(4.4f, 0.2f), 0f, 1.2f);
            c.Spawn("ambusher", "ambusher", new Vector2(4.9f, 0.6f), -100f);
            c.Spawn("quill", "scout_quill", new Vector2(5.0f, 3.6f), 220f);
            c.Spawn("sk", "sidekick", new Vector2(-1.6f, -2.2f), 30f);
            s.At(0f, cx => cx["ambusher"]?.Play("kneel", 30f));
            s.Caption(0.1f, "Everyone here is planning something. Read the room:");
            s.At(1.0f, cx =>
            {
                cx["sk"].Play("folded", 1.2f);
                cx.Overlay.SkillCallout(cx["sk"], "read_the_room", 1.4f);
                cx.Ring("read", new Vector2(-1.6f, -2.2f), 6.5f, new Color(0.48f, 0.9f, 1f, 0.35f));
                DemoContext.Sound("ui_confirm", 0.3f);
            });
            s.At(1.4f, cx =>
            {
                float shown = Mathf.Min(lasts, 7f);
                cx.Overlay.Bubble(cx["brute"], "HEAVY BLOW NEXT", shown);
                cx.Overlay.Bubble(cx["turncoat"], "WILL FEIGN SURRENDER", shown);
                cx.Overlay.Bubble(cx["ambusher"], "AMBUSH", shown);
                cx.Overlay.Mark(cx["ambusher"], "alert", shown, new Color(1f, 0.4f, 0.32f), 30f);
            });
            s.Caption(1.5f, $"For {N(lasts)} s, every foe within {N(radius)} m shows what he means to do. The one in the bush is marked where he lies.");
            s.At(4.4f, cx =>
            {
                cx.Overlay.Mark(cx["quill"], "pendant", 2.4f, new Color(0.75f, 0.78f, 0.82f));
                cx.Overlay.Bubble(cx["quill"], "Clever. Too clever. I'll be going.", 2.4f);
            });
            s.Caption(4.5f, "A scout's dull pendant shows too. Caught out, he leaves without a word to his master.");
            s.Walk("quill", 5.4f, 7.0f, new Vector2(5.0f, 3.6f), new Vector2(7.6f, 5.4f));
            s.At(7.0f, cx => cx.Hide("quill"));
            s.At(1.0f + lasts, cx => cx.RemoveDecal("read"));
            s.Caption(7.4f, "He never notices you doing it.");
            return s.End(Mathf.Max(9.6f, 1.6f + lasts));
        }

        // ======================================================================================== Read Runes
        static DemoScript ReadRunes(DemoContext c)
        {
            float channel = c.Def != null ? c.Def.A(c.Rank) : 2f, reach = c.Def != null ? c.Def.B(c.Rank) : 3f;
            var s = new DemoScript();
            var seal = new Vector2(2.6f, 2.0f);
            var callum = c.Spawn("callum", "callum", new Vector2(-3.4f, 1.0f), 70f);
            c.Spawn("sk", "sidekick", new Vector2(-4.6f, -2.0f), 45f);
            GameObject slab = null;
            s.At(0f, cx =>
            {
                slab = cx.Block("seal", seal, new Vector3(3.0f, 2.8f, 0.5f), Stone);
                cx.Block("runes", seal + new Vector2(0f, -0.27f), new Vector3(1.6f, 1.2f, 0.06f), Rune, 0.7f);
                cx.Ring("glow", seal + new Vector2(0f, -0.8f), 1.4f, new Color(0.62f, 0.45f, 1f, 0.45f));
            });
            s.Caption(0.1f, "The way on is sealed with runes. Left alone, he'll put his shoulder to it, and the ward will answer.");
            s.At(0.6f, cx => cx.Overlay.Bubble(callum, "Stand back. I'll see to this door.", 2.2f));
            s.Walk("sk", 1.0f, 2.6f, new Vector2(-4.6f, -2.0f), seal + new Vector2(-0.6f, -1.6f));
            float done = 2.7f + channel;
            s.At(2.7f, cx =>
            {
                cx["sk"].Face(seal);
                cx["sk"].Play("kneel", channel);
                cx.Overlay.SkillCallout(cx["sk"], "read_runes", 1.4f);
            });
            s.Caption(2.8f, $"Within {N(reach)} m, kneel and read: a {N(channel)} s channel. Stay still.");
            s.Over(2.7f, done - 0.02f, (cx, u) => cx.Overlay.Bar("channel", cx["sk"], u, Cyan));
            s.At(done, cx =>
            {
                cx.Overlay.RemoveBar("channel");
                cx["sk"].Play("none");
                cx.RemoveDecal("runes");
                cx.RemoveDecal("glow");
                Vfx.Burst(VfxKind.Glint, cx.Stage.World(seal) + Vector3.up * 1.4f, 1.4f);
                DemoContext.Sound("ui_confirm", 0.35f);
            });
            s.Over(done, done + 0.8f, (cx, u) =>
            {
                if (slab != null) slab.transform.position = cx.Stage.World(seal) + Vector3.up * (1.4f - 2.9f * u);
            });
            s.Caption(done + 0.1f, "The seal goes dark and the way opens. He finds scholarship faintly suspicious, and very useful.");
            s.Walk("callum", done + 1.0f, done + 3.0f, new Vector2(-3.4f, 1.0f), seal + new Vector2(0f, 0.8f));
            return s.End(done + 4.0f);
        }

        // ========================================================================================= Lockpick
        static DemoScript Lockpick(DemoContext c)
        {
            float channel = c.Def != null ? c.Def.A(c.Rank) : 3f, cut = c.Def != null ? c.Def.B(c.Rank) : 0f;
            bool rank2 = c.Rank >= 2;
            var s = new DemoScript();
            var gate = new Vector2(2.6f, 2.0f);
            var plate = new Vector2(-1.2f, -0.6f);
            c.Spawn("callum", "callum", new Vector2(-4.0f, 1.2f), 70f);
            c.Spawn("sk", "sidekick", new Vector2(-4.4f, -2.4f), 45f);
            GameObject bars = null;
            s.At(0f, cx =>
            {
                bars = cx.Block("gate", gate, new Vector3(3.0f, 2.6f, 0.25f), Iron);
                cx.Block("plate", plate, new Vector3(0.9f, 0.06f, 0.9f), new Color(0.55f, 0.5f, 0.42f, 1f));
                cx.Ring("plate_ring", plate, 0.75f, Red);
            });
            s.Caption(0.1f, "An iron gate, and a pressure plate on the way to it.");
            float trap = 1.0f;
            if (rank2)
            {
                s.Walk("sk", 0.3f, 0.9f, new Vector2(-4.4f, -2.4f), plate + new Vector2(-2.2f, -0.7f));
                s.At(trap, cx =>
                {
                    cx["sk"].Face(plate);
                    cx["sk"].Play("interact", 0.5f);
                    cx.Overlay.SkillCallout(cx["sk"], "lockpick", 1.2f);
                    cx.RemoveDecal("plate_ring");
                    Vfx.Burst(VfxKind.Sparks, cx.Stage.World(plate) + Vector3.up * 0.2f, 0.6f);
                    DemoContext.Sound("click", 0.45f);
                });
                s.Caption(trap + 0.1f, $"At rank II, a trap you can see is cut from {N(cut)} m away, at once.");
            }
            float walkFrom = rank2 ? 1.8f : 0.6f;
            var start = rank2 ? plate + new Vector2(-2.2f, -0.7f) : new Vector2(-4.4f, -2.4f);
            s.Walk("sk", walkFrom, walkFrom + 1.4f, start, gate + new Vector2(-0.5f, -1.4f));
            float t0 = walkFrom + 1.5f, done = t0 + channel;
            s.At(t0, cx =>
            {
                cx["sk"].Face(gate);
                cx["sk"].Play("kneel", channel);
                if (!rank2) cx.Overlay.SkillCallout(cx["sk"], "lockpick", 1.4f);
                DemoContext.Sound("click", 0.35f);
            });
            s.Caption(t0 + 0.1f, $"Kneel at the gate and pick it: a {N(channel)} s channel. He doesn't ask where you learned it.");
            s.Over(t0, done - 0.02f, (cx, u) => cx.Overlay.Bar("channel", cx["sk"], u, Cyan));
            s.At(done, cx =>
            {
                cx.Overlay.RemoveBar("channel");
                cx["sk"].Play("none");
                DemoContext.Sound("latch", 0.45f);
            });
            s.Over(done, done + 0.9f, (cx, u) =>
            {
                if (bars != null) bars.transform.position = cx.Stage.World(gate) + Vector3.up * (1.3f + 2.4f * u);
            });
            s.Caption(done + 0.2f, rank2 ? "The gate swings up. The way is clear." : "The gate swings up. Rank II also cuts a trap you can see from a step away.");
            return s.End(done + 3.0f);
        }

        // ======================================================================================== Map Sketch
        static DemoScript MapSketch(DemoContext c)
        {
            float lasts = c.Def != null ? c.Def.A(c.Rank) : 10f, finds = c.Def != null ? c.Def.B(c.Rank) : 30f;
            var s = new DemoScript();
            var path = new[] { new Vector2(-5.4f, 0.4f), new Vector2(-2.0f, 1.4f), new Vector2(1.2f, 0.4f), new Vector2(5.6f, 1.2f) };
            var plateA = new Vector2(-0.4f, 0.9f);
            var plateB = new Vector2(3.4f, 0.8f);
            var cache = new Vector2(4.4f, -2.6f);
            c.Spawn("callum", "callum", path[0], 75f);
            c.Spawn("sk", "sidekick", new Vector2(-4.6f, -2.0f), 60f);
            s.Caption(0.1f, "Charcoal, a scrap of vellum and a good guess:");
            s.At(0.8f, cx =>
            {
                cx["sk"].Play("interact", 1.0f);
                cx.Overlay.SkillCallout(cx["sk"], "map_sketch", 1.4f);
                DemoContext.Sound("cloth", 0.3f);
            });
            s.At(1.4f, cx =>
            {
                for (int i = 0; i + 1 < path.Length; i++)
                    cx.Line("path" + i, cx.Stage.World(path[i]) + Vector3.up * 0.08f, cx.Stage.World(path[i + 1]) + Vector3.up * 0.08f, Chalk, 0.07f);
                cx.Block("plateA", plateA, new Vector3(0.9f, 0.05f, 0.9f), new Color(0.55f, 0.5f, 0.42f, 1f));
                cx.Ring("plateA_ring", plateA, 0.75f, Red);
                cx.Block("plateB", plateB, new Vector3(0.9f, 0.05f, 0.9f), new Color(0.55f, 0.5f, 0.42f, 1f));
                cx.Ring("plateB_ring", plateB, 0.75f, Red);
                cx.Ring("cache", cache, 0.6f, new Color(0.48f, 0.9f, 1f, 0.7f));
                cx.Block("cache_box", cache, new Vector3(0.6f, 0.4f, 0.4f), new Color(0.5f, 0.36f, 0.2f, 1f));
            });
            s.Caption(1.5f, $"For {N(lasts)} s his road ahead shows as a line, and hidden plates and caches within {N(finds)} m are found.");
            s.Walk("sk", 3.4f, 4.6f, new Vector2(-4.6f, -2.0f), plateA + new Vector2(-0.3f, -0.9f));
            s.At(4.7f, cx =>
            {
                cx["sk"].Face(plateA);
                cx["sk"].Play("kneel", 1.2f);
            });
            s.At(5.9f, cx =>
            {
                cx.RemoveDecal("plateA_ring");
                cx["sk"].Play("none");
                DemoContext.Sound("click", 0.4f);
            });
            s.Caption(4.6f, "He'll walk his line regardless. Now you know where it crosses a plate.");
            s.Over(6.2f, 9.2f, (cx, u) =>
            {
                var p = cx["callum"];
                if (p == null) return;
                float f = u * 2f;
                int i = Mathf.Min(1, Mathf.FloorToInt(f));
                var a = path[i];
                var b = path[i + 1];
                p.Pos = Vector2.Lerp(a, b, f - i);
                p.Face(b);
                p.Locomotion(u < 1f ? 2.6f : 0f);
            });
            s.Caption(9.0f, "He never notices you doing it.");
            s.At(1.4f + lasts, cx =>
            {
                for (int i = 0; i + 1 < path.Length; i++) cx.RemoveDecal("path" + i);
            });
            return s.End(Mathf.Max(11.2f, 1.6f + lasts));
        }

        // ========================================================================================== Buckler
        static DemoScript Buckler(DemoContext c)
        {
            float raised = c.Def != null ? c.Def.A(c.Rank) : 1.2f, cover = c.Def != null ? c.Def.B(c.Rank) : 0f;
            bool rank2 = c.Rank >= 2;
            var s = new DemoScript();
            var callum = c.Spawn("callum", "callum", new Vector2(-2.6f, 1.6f), 90f);
            c.Spawn("cultist", "cultist", new Vector2(-0.8f, 1.6f), -90f);
            c.Spawn("thug", "thug", new Vector2(1.8f, -1.6f), -100f);
            c.Spawn("sk", "sidekick", new Vector2(0.0f, -1.8f), 80f);
            c.Prop("perch", "crate_perch", new Vector2(4.4f, 3.4f), 0f);
            var xbow = c.Spawn("xbow", "crossbowman", new Vector2(4.4f, 3.4f), 235f);
            if (xbow != null)
            {
                xbow.Height = 0.95f;
                xbow.Pos = xbow.Pos;
            }
            s.At(0f, cx =>
            {
                callum.Flag("combat", true);
                cx["cultist"].Flag("combat", true);
                cx["thug"].Flag("combat", true);
            });
            Duel(s, "callum", "cultist", 0.3f, 3.6f);
            s.Caption(0.1f, "A blow coming at you from the front. Raise the buckler:");
            s.At(0.9f, cx => cx["thug"].Play("attack", 0.8f));
            s.At(1.0f, cx =>
            {
                cx["sk"].Face(cx["thug"].Pos);
                cx["sk"].Play("block", raised);
                cx.Overlay.SkillCallout(cx["sk"], "buckler", 1.4f);
            });
            s.At(1.4f, cx =>
            {
                Vfx.Burst(VfxKind.Sparks, Chest(cx["sk"]) + cx["sk"].Root.forward * 0.4f, 0.8f);
                cx["thug"].Play("stagger", 1f);
                cx.Overlay.Mark(cx["thug"], "question", 1f);
                DemoContext.Sound("riposte", 0.4f);
            });
            s.Caption(1.5f, $"Raised for {N(raised)} s: a blow from the front glances off, and he reels for 1 s.");
            float shot = 4.6f;
            s.Walk("sk", 3.0f, 4.0f, new Vector2(0.0f, -1.8f), new Vector2(-2.0f, 0.6f));
            s.At(shot - 0.9f, cx =>
            {
                cx["xbow"].Play("aim");
                cx.Line("aim", Chest(cx["xbow"]), Chest(callum), Red, 0.04f);
            });
            if (rank2)
            {
                s.At(shot - 0.2f, cx =>
                {
                    cx["sk"].Face(cx["xbow"].Pos);
                    cx["sk"].Play("block", raised);
                });
                Bolt(s, "bolt", cx => Chest(cx["xbow"]), cx => Chest(cx["sk"]) + cx["sk"].Root.forward * 0.35f, shot, shot + 0.25f);
                s.At(shot + 0.25f, cx =>
                {
                    cx.RemoveDecal("aim");
                    Vfx.Burst(VfxKind.Sparks, Chest(cx["sk"]) + cx["sk"].Root.forward * 0.4f, 0.8f);
                    DemoContext.Sound("thunk", 0.4f);
                });
                s.Caption(shot - 0.6f, $"At rank II, standing within {N(cover)} m of him, it takes a shot meant for him.");
            }
            else
            {
                Bolt(s, "bolt", cx => Chest(cx["xbow"]), cx => Chest(callum), shot, shot + 0.3f);
                s.At(shot + 0.3f, cx =>
                {
                    cx.RemoveDecal("aim");
                    callum.Play("hit", 0.4f);
                    DemoContext.Sound("thunk", 0.4f);
                });
                s.Caption(shot - 0.6f, "Shots at him get past it at rank I. Rank II takes them, if you stand close.");
            }
            s.Caption(shot + 1.6f, "Defending yourself is your business. Defending him, he pretends not to notice.");
            return s.End(shot + 4.0f);
        }
    }
}
