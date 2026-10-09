using System.Collections.Generic;
using HS.Presentation;
using UnityEngine;

namespace HS.Tutorial.Demo
{
    /// <summary>The Bastion's tricks (Bait &amp; Switch, Smoke Bomb, Pep Talk, Shoulder Check) and the four capstones (Domino
    /// Effect, Crossfire, Hold Please, Silent Partner). Draft captions for the owner to rewrite.</summary>
    public static partial class SkillDemos
    {
        static readonly Color Smoke = new Color(0.78f, 0.8f, 0.84f, 0.55f);

        /// <summary>A barrel stack comes down on its impact point at t (as Loosen Bolt's does).</summary>
        static void Collapse(DemoScript s, string propId, Vector2 impact, float t)
        {
            var barrels = new List<(Transform tr, Vector3 from, Vector3 to, Quaternion r0)>();
            s.At(t, cx =>
            {
                if (!cx.Props.TryGetValue(propId, out var stack) || stack == null) return;
                var at = cx.Stage.World(impact);
                foreach (Transform tr in stack.transform)
                    if (tr.name == "Barrel")
                    {
                        var to = at + new Vector3(0.5f * barrels.Count - 0.5f, 0f, 0.3f - 0.3f * barrels.Count);
                        to.y = DemoStage.Origin.y + 0.45f;
                        barrels.Add((tr, tr.position, to, tr.rotation));
                    }
                DemoContext.Sound("collapse", 0.45f);
            });
            s.Over(t, t + 0.55f, (cx, u) =>
            {
                foreach (var (tr, from, to, r0) in barrels)
                {
                    if (tr == null) continue;
                    var p = Vector3.Lerp(from, to, u);
                    p.y = Mathf.Lerp(from.y, to.y, u * u);
                    tr.position = p;
                    tr.rotation = r0 * Quaternion.Euler(0f, 0f, -95f * u);
                }
            });
            s.At(t + 0.55f, cx =>
            {
                Vfx.Burst(VfxKind.Dust, cx.Stage.World(impact) + Vector3.up * 0.3f, 2f);
                cx.RemoveDecal("ring_" + propId);
            });
        }

        // ==================================================================================== Bait & Switch
        static DemoScript BaitAndSwitch(DemoContext c)
        {
            float lure = c.Def != null ? c.Def.A(c.Rank) : 4f, reach = c.Def != null ? c.Def.B(c.Rank) : 8f;
            var s = new DemoScript();
            var callum = c.Spawn("callum", "callum", new Vector2(-2.8f, 1.6f), 90f);
            c.Spawn("duel", "thug", new Vector2(-1.0f, 1.6f), -90f);
            c.Spawn("a", "thug", new Vector2(5.6f, 3.2f), 230f);
            c.Spawn("b", "brute", new Vector2(6.0f, 0.2f), 270f);
            c.Spawn("sk", "sidekick", new Vector2(-1.4f, -2.6f), 60f);
            var decoyAt = new Vector2(2.6f, -1.6f);
            s.At(0f, cx =>
            {
                callum.Flag("combat", true);
                cx["duel"].Flag("combat", true);
                cx["a"].Flag("combat", true);
                cx["b"].Flag("combat", true);
            });
            float gone = 1.6f + lure;
            Duel(s, "callum", "duel", 0.3f, gone + 2.4f);
            s.Caption(0.1f, "Two more are coming for his back.");
            s.Walk("a", 0.2f, 1.6f, new Vector2(5.6f, 3.2f), new Vector2(3.6f, 2.6f));
            s.Walk("b", 0.2f, 1.6f, new Vector2(6.0f, 0.2f), new Vector2(4.0f, 0.8f));
            s.At(1.2f, cx =>
            {
                cx["sk"].Face(decoyAt);
                cx["sk"].Play("throw", 0.6f);
                cx.Overlay.SkillCallout(cx["sk"], "bait_and_switch", 1.4f);
            });
            s.At(1.6f, cx =>
            {
                var d = cx.Spawn("decoy", "sidekick", decoyAt, 40f);
                d?.Play("folded", lure);
                cx.Ring("lure", decoyAt, Mathf.Min(reach, 4.5f), new Color(0.48f, 0.9f, 1f, 0.4f));
                Vfx.Burst(VfxKind.Dust, cx.Stage.World(decoyAt) + Vector3.up * 0.4f, 0.8f);
                DemoContext.Sound("cloth", 0.4f);
            });
            s.Caption(1.7f, $"A decoy in your shape. Everyone within {N(reach)} m of it goes for it, for {N(lure)} s.");
            s.Walk("a", 1.9f, 2.9f, new Vector2(3.6f, 2.6f), decoyAt + new Vector2(0.4f, 1.1f));
            s.Walk("b", 1.9f, 2.9f, new Vector2(4.0f, 0.8f), decoyAt + new Vector2(1.2f, 0.2f));
            for (float t = 3.0f; t < gone - 0.3f; t += 0.9f)
            {
                bool first = t < 3.1f;
                s.At(t, cx =>
                {
                    cx["a"].Face(decoyAt);
                    cx["b"].Face(decoyAt);
                    cx[first ? "a" : "b"].Play("attack", 0.6f);
                });
            }
            s.Caption(3.4f, "All but the man in his duel: that one has eyes only for him.");
            s.At(gone, cx =>
            {
                cx.Hide("decoy");
                cx.RemoveDecal("lure");
                Vfx.Burst(VfxKind.Dust, cx.Stage.World(decoyAt) + Vector3.up * 0.4f, 0.8f);
                cx.Overlay.Mark(cx["a"], "question", 1.2f);
                cx.Overlay.Mark(cx["b"], "question", 1.2f);
            });
            s.Caption(gone + 0.1f, "No dishonour in a scarecrow. He never even asks.");
            return s.End(gone + 2.6f);
        }

        // ======================================================================================== Smoke Bomb
        static DemoScript SmokeBomb(DemoContext c)
        {
            float lasts = c.Def != null ? c.Def.A(c.Rank) : 6f, radius = c.Def != null ? c.Def.B(c.Rank) : 3f;
            var s = new DemoScript();
            var callum = c.Spawn("callum", "callum", new Vector2(-4.6f, 0.6f), 90f);
            c.Spawn("thug", "thug", new Vector2(-2.8f, 0.6f), -90f);
            c.Prop("perch", "crate_perch", new Vector2(4.2f, 2.6f), 0f);
            var xbow = c.Spawn("xbow", "crossbowman", new Vector2(4.2f, 2.6f), 245f);
            if (xbow != null)
            {
                xbow.Height = 0.95f;
                xbow.Pos = xbow.Pos;
            }
            c.Spawn("sk", "sidekick", new Vector2(-0.4f, -2.6f), 45f);
            var cloud = new Vector2(0.8f, 1.5f);
            s.At(0f, cx =>
            {
                callum.Flag("combat", true);
                cx["thug"].Flag("combat", true);
                cx.Cone("cone", callum, 120f, 7f);
                cx["xbow"].Play("aim");
                cx.Line("aim", Chest(cx["xbow"]), Chest(callum), Red, 0.04f);
            });
            float puff = 1.6f, clears = puff + lasts;
            Duel(s, "callum", "thug", 0.3f, clears + 1.6f);
            s.Caption(0.1f, "A bowman has a clean line on him. Throw smoke between them:");
            s.At(1.1f, cx =>
            {
                cx["sk"].Face(cloud);
                cx["sk"].Play("throw", 0.6f);
                cx.Overlay.SkillCallout(cx["sk"], "smoke_bomb", 1.4f);
            });
            Throw(s, "bomb", "sk", cx => cx.Stage.World(cloud) + Vector3.up * 0.4f, 1.25f, puff);
            s.At(puff, cx =>
            {
                cx.Ring("smoke", cloud, Mathf.Min(radius, 3.4f), Smoke);
                cx.RemoveDecal("aim");
                cx["xbow"].Play("none");
                cx.Overlay.Mark(cx["xbow"], "question", 1.6f);
                DemoContext.Sound("syn_whoosh", 0.5f);
            });
            for (float t = puff; t < clears; t += 0.7f)
                s.At(t, cx =>
                {
                    var at = cx.Stage.World(cloud);
                    for (int k = 0; k < 3; k++)
                    {
                        float a = (k * 120f + cx.Time * 40f) * Mathf.Deg2Rad;
                        Vfx.Burst(VfxKind.Dust, at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * 0.5f + Vector3.up * (0.6f + 0.5f * k), radius * 0.6f);
                    }
                });
            s.Caption(puff + 0.1f, $"A cloud {N(radius * 2f)} m across, for {N(lasts)} s. Nothing sees through it: not him, not a stone, not a man with a bow.");
            s.Caption(puff + 3.2f, "Anyone inside it loses track of you. Smoke is weather, and he has never been offended by weather.");
            s.At(clears, cx =>
            {
                cx.RemoveDecal("smoke");
                cx["xbow"].Play("aim");
                cx.Line("aim", Chest(cx["xbow"]), Chest(callum), Red, 0.04f);
            });
            return s.End(clears + 1.4f);
        }

        // ========================================================================================= Pep Talk
        static DemoScript PepTalk(DemoContext c)
        {
            float bonus = c.Def != null ? c.Def.A(c.Rank) : 0.15f, lasts = c.Def != null ? c.Def.B(c.Rank) : 8f;
            var s = new DemoScript();
            var callum = c.Spawn("callum", "callum", new Vector2(-1.4f, 1.0f), 90f);
            c.Spawn("thug", "woodsman", new Vector2(0.4f, 1.0f), -90f);
            c.Spawn("sk", "sidekick", new Vector2(-4.4f, -2.0f), 45f);
            float hp = 1f;
            s.At(0f, cx =>
            {
                callum.Flag("combat", true);
                cx["thug"].Flag("combat", true);
                cx.Overlay.Bar("hp", cx["thug"], hp, HpRed);
            });
            Duel(s, "callum", "thug", 0.3f, 2.4f);
            s.Caption(0.1f, "Within earshot (14 m), a word of encouragement:");
            s.At(1.2f, cx =>
            {
                cx["sk"].Face(callum.Pos);
                cx["sk"].Play("talk", 1.6f);
                cx.Overlay.SkillCallout(cx["sk"], "pep_talk", 1.4f);
                cx.Overlay.Bubble(cx["sk"], "You've got him, sir! Left side's soft!", 2.2f);
            });
            float lift = 2.6f, ends = lift + lasts;
            s.At(lift, cx =>
            {
                cx.Overlay.Bubble(callum, "...Yes. Yes! Onward!", 2f);
                cx.Overlay.Mark(callum, "fight", lasts, HonorGold, 12f);
                Vfx.Burst(VfxKind.Glint, callum.Root.position + Vector3.up * 2f, 0.9f);
                DemoContext.Sound("ui_confirm", 0.3f);
            });
            // Inspired: his blows come quicker and land harder.
            float step = 0.85f / (1f + bonus);
            int k = 0;
            for (float t = lift + 0.2f; t < ends; t += step, k++)
            {
                bool his = k % 2 == 0;
                s.At(t, cx =>
                {
                    cx[his ? "callum" : "thug"]?.Play(his ? "attack" : "attack2", 0.6f);
                    if (!his) return;
                    hp = Mathf.Max(0.08f, hp - 0.07f * (1f + bonus));
                    cx.Overlay.Bar("hp", cx["thug"], hp, HpRed);
                    DemoContext.Sound("sword", 0.25f);
                });
            }
            s.Caption(lift + 0.1f, $"+{N(bonus * 100f)}% damage and speed for {N(lasts)} s.");
            s.Caption(lift + 3.6f, "He pretends he didn't need it. He fights better anyway.");
            return s.End(ends + 1.0f);
        }

        // ===================================================================================== Shoulder Check
        static DemoScript ShoulderCheck(DemoContext c)
        {
            float knock = c.Def != null ? c.Def.A(c.Rank) : 3f, reel = c.Def != null ? c.Def.B(c.Rank) : 1.2f;
            var s = new DemoScript();
            var callum = c.Spawn("callum", "callum", new Vector2(-3.2f, 2.4f), 90f);
            c.Spawn("duel", "cultist", new Vector2(-1.4f, 2.4f), -90f);
            c.Spawn("thug", "thug", new Vector2(0.8f, -0.8f), -90f);
            c.Spawn("sk", "sidekick", new Vector2(-1.4f, -0.8f), 90f);
            var hitAt = new Vector2(-0.1f, -0.8f);
            var flung = new Vector2(0.8f + knock, -0.8f);
            s.At(0f, cx =>
            {
                callum.Flag("combat", true);
                cx["duel"].Flag("combat", true);
                cx["thug"].Flag("combat", true);
                cx.Cone("cone", callum, 120f, 7f);
            });
            Duel(s, "callum", "duel", 0.3f, 9.0f);
            s.Caption(0.1f, "A foe close in front: lead with the shoulder.");
            s.At(0.7f, cx => cx.Overlay.SkillCallout(cx["sk"], "shoulder_check", 1.4f));
            s.Walk("sk", 0.9f, 1.15f, new Vector2(-1.4f, -0.8f), hitAt);
            s.At(1.15f, cx =>
            {
                cx["sk"].Play("heavy", 0.5f);
                cx["thug"].Play("stagger", reel);
                Vfx.Burst(VfxKind.Hit, Chest(cx["thug"]), 1f);
                DemoContext.Sound("punch", 0.5f);
            });
            s.Over(1.15f, 1.45f, (cx, u) =>
            {
                var p = cx["thug"];
                if (p != null) p.Pos = Vector2.Lerp(new Vector2(0.8f, -0.8f), flung, 1f - (1f - u) * (1f - u));
            });
            s.At(1.5f, cx => cx.Overlay.Mark(cx["thug"], "question", reel));
            s.Caption(1.3f, $"{N(knock)} m of knockback, and he reels for {N(reel)} s.");
            s.Caption(4.0f, "A scuffle of your own is your business. Shove the man in his duel, and it's a slight:");
            s.Walk("sk", 4.4f, 5.4f, hitAt, new Vector2(-1.4f, 1.3f));
            s.At(5.5f, cx =>
            {
                cx["sk"].Face(cx["duel"].Pos);
                cx["sk"].Play("heavy", 0.5f);
                cx["duel"].Play("stagger", reel);
                DemoContext.Sound("punch", 0.45f);
            });
            s.At(5.8f, cx => cx.Overlay.Bubble(callum, "Let a man finish his own fight.", 2.6f));
            s.Over(5.8f, 6.3f, (cx, u) => cx.Overlay.Bar("honor", callum, Mathf.Lerp(1f, 0.92f, u), HonorGold, 34f));
            return s.End(9.0f);
        }

        // ===================================================================================== Domino Effect
        static DemoScript DominoEffect(DemoContext c)
        {
            float rigs = c.Def != null ? c.Def.A(c.Rank) : 2f, reach = c.Def != null ? c.Def.B(c.Rank) : 40f;
            var s = new DemoScript();
            var impacts = new[] { new Vector2(-1.0f, 2.0f), new Vector2(2.4f, 2.6f), new Vector2(5.0f, 0.4f) };
            c.Prop("s0", "barrel_stack", impacts[0] + new Vector2(-1.6f, 0.4f), 0f);
            c.Prop("s1", "barrel_stack", impacts[1] + new Vector2(-1.4f, 0.6f), 0f);
            c.Prop("s2", "barrel_stack", impacts[2] + new Vector2(-1.5f, 0.5f), 0f);
            c.Spawn("callum", "callum", new Vector2(-5.0f, -0.6f), 70f);
            c.Spawn("t0", "thug", impacts[0], 200f);
            c.Spawn("t1", "brute", impacts[1], 210f);
            c.Spawn("t2", "thug", impacts[2], 240f);
            c.Spawn("sk", "sidekick", new Vector2(-0.4f, -1.6f), 60f);
            s.At(0f, cx => cx.Ring("ring_s2", impacts[2], 1.9f, Red));
            s.Caption(0.1f, "One prop is rigged already (the red ring is where it lands). The rest are not.");
            s.At(1.0f, cx =>
            {
                cx.Overlay.Key(cx["sk"], "capstone", 1.6f);
                cx.Overlay.SkillCallout(cx["sk"], "domino_effect", 1.6f);
                cx["sk"].Play("throw", 0.6f);
            });
            s.At(1.3f, cx =>
            {
                cx.Ring("ring_s0", impacts[0], 1.9f, Red);
                cx.Ring("ring_s1", impacts[1], 1.9f, Red);
                DemoContext.Sound("latch", 0.4f);
            });
            s.Caption(1.3f, KeyGlyphs.Format($"Press {{capstone}}: the {N(rigs)} props nearest you are rigged on the spot..."));
            Collapse(s, "s0", impacts[0], 2.2f);
            Collapse(s, "s1", impacts[1], 2.3f);
            Collapse(s, "s2", impacts[2], 2.4f);
            s.At(2.8f, cx =>
            {
                foreach (var id in new[] { "t0", "t1", "t2" }) cx[id]?.Play("hit_heavy");
                DemoContext.Sound("heavy", 0.5f);
            });
            s.At(3.3f, cx =>
            {
                foreach (var id in new[] { "t0", "t1", "t2" }) cx[id]?.Play("death");
            });
            s.Caption(2.4f, $"...then everything rigged within {N(reach)} m comes down at once.");
            s.Caption(4.8f, "A collapse he sees is a dirty trick, however many there are.");
            return s.End(7.6f);
        }

        // ========================================================================================= Crossfire
        static DemoScript Crossfire(DemoContext c)
        {
            float baseBolt = c.Def != null ? c.Def.A(c.Rank) : 20f, perRank = c.Def != null ? c.Def.B(c.Rank) : 10f;
            var s = new DemoScript();
            var callum = c.Spawn("callum", "callum", new Vector2(-2.4f, 1.8f), 90f);
            c.Spawn("duel", "brute", new Vector2(-0.6f, 1.8f), -90f);
            c.Spawn("xbow", "crossbowman", new Vector2(4.4f, -0.6f), 250f);
            c.Spawn("sk", "sidekick", new Vector2(-1.6f, -2.4f), 60f);
            s.At(0f, cx =>
            {
                callum.Flag("combat", true);
                cx["duel"].Flag("combat", true);
                cx.Overlay.Bar("hp", cx["xbow"], 1f, HpRed);
                cx.Overlay.Bar("hp2", cx["duel"], 0.5f, HpRed);
            });
            Duel(s, "callum", "duel", 0.3f, 4.6f);
            s.Caption(0.1f, KeyGlyphs.Format("Press {capstone}: three bolts at the aim point."));
            s.At(0.8f, cx =>
            {
                cx["sk"].Face(cx["xbow"].Pos);
                cx["sk"].Play("shoot", 0.5f);
                cx.Overlay.SkillCallout(cx["sk"], "crossfire", 1.4f);
            });
            for (int k = 0; k < 3; k++)
            {
                float t = 0.9f + 0.12f * k;
                var off = new Vector3((k - 1) * 0.35f, 0f, 0f);
                Bolt(s, "v" + k, cx => Chest(cx["sk"]) + Vector3.up * 0.1f, cx => Chest(cx["xbow"]) + off, t, t + 0.22f);
            }
            s.At(1.3f, cx =>
            {
                cx["xbow"].Play("hit_heavy");
                cx.Overlay.Bar("hp", cx["xbow"], 0.1f, HpRed);
                DemoContext.Sound("thunk", 0.5f);
            });
            s.Caption(1.4f, $"Each bolt deals {N(baseBolt)}, plus {N(perRank)} for every rank of your fighting tricks (the knife counts one).");
            float judgment = 4.8f, blow = judgment + 2.0f;
            s.At(judgment, cx =>
            {
                callum.Play("guard", 2f);
                cx.Overlay.Mark(callum, "judgment", 2.2f, HonorGold);
                cx.Overlay.Bubble(callum, "Judgment.", 1.8f);
            });
            s.Caption(judgment, "While he gathers his Judgment, the volley goes into his target instead...");
            s.At(blow - 0.35f, cx =>
            {
                cx["sk"].Face(cx["duel"].Pos);
                cx["sk"].Play("shoot", 0.5f);
            });
            for (int k = 0; k < 3; k++)
            {
                float t = blow - 0.3f + 0.08f * k;
                var off = new Vector3(0f, 0.15f * (k - 1), 0f);
                Bolt(s, "j" + k, cx => Chest(cx["sk"]) + Vector3.up * 0.1f, cx => Chest(cx["duel"]) + off, t, blow);
            }
            s.At(blow, cx =>
            {
                callum.Play("attack3", 0.6f);
                Vfx.Burst(VfxKind.Sparks, Chest(cx["duel"]), 1.6f);
                cx.Overlay.Bar("hp2", cx["duel"], 0f, HpRed);
                DemoContext.Sound("heavy", 0.5f);
            });
            s.At(blow + 0.4f, cx => cx["duel"].Play("death"));
            s.Caption(blow + 0.1f, "...and lands with his blow. Hard to say whose it was; he doesn't ask.");
            return s.End(blow + 3.0f);
        }

        // ======================================================================================== Hold Please
        static DemoScript HoldPlease(DemoContext c)
        {
            float hold = c.Def != null ? c.Def.A(c.Rank) : 4f, reach = c.Def != null ? c.Def.B(c.Rank) : 40f;
            var s = new DemoScript();
            var callum = c.Spawn("callum", "callum", new Vector2(-1.8f, 1.6f), 90f);
            c.Spawn("duel", "thug", new Vector2(0.0f, 1.6f), -90f);
            c.Spawn("turncoat", "turncoat", new Vector2(-4.4f, 3.0f), 120f);
            c.Spawn("sk", "sidekick", new Vector2(-1.0f, -2.2f), 30f);
            var everyone = new[] { "callum", "duel", "turncoat" };
            s.At(0f, cx =>
            {
                callum.Flag("combat", true);
                cx["duel"].Flag("combat", true);
                cx["turncoat"].Flag("combat", true);
            });
            Duel(s, "callum", "duel", 0.3f, 1.4f);
            s.Caption(0.1f, "Someone is creeping up on his back with a knife out.");
            s.Walk("turncoat", 0.2f, 1.4f, new Vector2(-4.4f, 3.0f), new Vector2(-3.2f, 2.2f));
            float on = 1.5f, off = on + hold;
            s.At(on, cx =>
            {
                cx.Overlay.Key(cx["sk"], "capstone", 1.6f);
                cx.Overlay.SkillCallout(cx["sk"], "hold_please", 1.6f);
                cx.Overlay.Bubble(cx["sk"], "Hold, please.", 1.8f);
                cx.Ring("held", new Vector2(-1.0f, -2.2f), 6.5f, new Color(0.48f, 0.9f, 1f, 0.3f));
                foreach (var id in everyone)
                {
                    cx[id]?.Flag("frozen", true);
                    cx.Overlay.Mark(cx[id], "wait", hold, Cyan);
                }
                Vfx.Burst(VfxKind.Glint, cx["sk"].Root.position + Vector3.up * 1.5f, 1.6f);
                DemoContext.Sound("ui_confirm", 0.4f);
            });
            s.Caption(on + 0.1f, $"Everyone within {N(reach)} m but you is held still for {N(hold)} s. Him too.");
            s.Walk("sk", on + 0.8f, on + 2.4f, new Vector2(-1.0f, -2.2f), new Vector2(-3.0f, 1.2f));
            s.At(on + 2.5f, cx =>
            {
                cx["sk"].Face(cx["turncoat"].Pos);
                cx["sk"].Play("interact", 0.8f);
            });
            s.Caption(on + 2.4f, "You are not held. Strike a held man where he can see, though, and that's striking the helpless.");
            s.At(off, cx =>
            {
                foreach (var id in everyone) cx[id]?.Flag("frozen", false);
                cx.RemoveDecal("held");
            });
            Duel(s, "callum", "duel", off + 0.2f, off + 2.6f);
            return s.End(off + 2.8f);
        }

        // ===================================================================================== Silent Partner
        static DemoScript SilentPartner(DemoContext c)
        {
            float regen = c.Def != null ? c.Def.A(c.Rank) : 0.006f, within = c.Def != null ? c.Def.B(c.Rank) : 12f;
            var s = new DemoScript();
            var callum = c.Spawn("callum", "callum", new Vector2(-0.6f, 0.8f), 120f);
            c.Spawn("sk", "sidekick", new Vector2(-2.2f, -0.6f), 60f);
            float h0 = 0.5f;
            // One sped-up minute near her: 60 s × the rate.
            float h1 = Mathf.Min(1f, h0 + regen * 60f);
            s.At(0f, cx =>
            {
                cx.Overlay.Bar("hp", callum, h0, HpRed);
                cx.Overlay.Mark(callum, "wound_arm", 6f, new Color(1f, 0.6f, 0.4f), 12f);
                cx.Ring("near", new Vector2(-2.2f, -0.6f), 4.5f, new Color(0.55f, 1f, 0.6f, 0.35f));
            });
            s.Caption(0.1f, "A passive: no key to press. Just stay near him.");
            s.Over(0.6f, 5.6f, (cx, u) => cx.Overlay.Bar("hp", callum, Mathf.Lerp(h0, h1, u), HpRed));
            s.Caption(0.8f, $"Within {N(within)} m of you he recovers {N(regen * 100f)}% of his health every second (a minute, sped up).");
            s.At(4.4f, cx =>
            {
                cx.Overlay.ClearMarks(callum);
                cx.Overlay.Mark(callum, "check", 1.6f, new Color(0.55f, 1f, 0.6f), 12f);
                Vfx.Burst(VfxKind.Heal, callum.Root.position + Vector3.up * 0.8f, 0.9f);
            });
            s.Caption(4.4f, "Every 45 s, one minor wound of his is quietly seen to. He has never once wondered why.");
            s.At(6.2f, cx => cx.RemoveDecal("near"));
            s.Walk("sk", 6.2f, 8.0f, new Vector2(-2.2f, -0.6f), new Vector2(-6.4f, -3.4f));
            s.Caption(8.0f, $"Beyond {N(within)} m, nothing.");
            return s.End(10.4f);
        }
    }
}
