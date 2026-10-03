using System;
using System.Collections.Generic;
using System.Globalization;
using HS.Presentation;
using UnityEngine;

namespace HS.Tutorial.Demo
{
    /// <summary>
    /// The "System simulation" for each implemented skill (spec §5.4): the real characters act it out on the demo stage,
    /// with captions in steps, and the numbers come from the skill's data at the shown rank. Each demo also shows how
    /// Callum's code reads the trick (in his sight or out of it). Draft captions for the owner to rewrite.
    /// Stage coordinates: x across (−7..7), y away from the camera (−4..5).
    /// </summary>
    public static class SkillDemos
    {
        static readonly Dictionary<string, Func<DemoContext, DemoScript>> Builders = new Dictionary<string, Func<DemoContext, DemoScript>>
        {
            { "pocket_sand", PocketSand },
            { "loosen_bolt", LoosenBolt },
            { "quiet_feet", QuietFeet },
            { "crossbow", Crossbow },
            { "bandage", Bandage },
            { "cover_story", CoverStory },
        };

        public static bool Has(string skillId) => skillId != null && Builders.ContainsKey(skillId);

        public static DemoScript Build(string skillId, DemoContext c) => Has(skillId) ? Builders[skillId](c) : null;

        static string N(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
        static readonly Color Pale = new Color(0.86f, 0.95f, 1f, 0.55f);
        static readonly Color Red = new Color(1f, 0.32f, 0.26f, 0.75f);
        static readonly Color HonorGold = new Color32(242, 193, 78, 255);
        static readonly Color HpRed = new Color32(214, 66, 58, 255);
        static readonly Color Cyan = new Color32(123, 230, 255, 255);

        /// <summary>Two duellists trading blows from t0 to t1 (he swings, they swing).</summary>
        static void Duel(DemoScript s, string a, string b, float t0, float t1)
        {
            int k = 0;
            for (float t = t0; t < t1; t += 0.85f, k++)
            {
                string who = k % 2 == 0 ? a : b, anim = k % 4 == 0 ? "attack" : k % 4 == 2 ? "attack2" : "attack";
                s.At(t, c => c[who]?.Play(anim, 0.7f));
                if (k % 2 == 0) s.At(t + 0.32f, c => DemoContext.Sound("sword", 0.25f));
            }
        }

        /// <summary>A thrown pouch in an arc from a puppet's hand to a point, landing at t1.</summary>
        static void Throw(DemoScript s, string id, string who, Func<DemoContext, Vector3> target, float t0, float t1)
        {
            Vector3 from = default, to = default;
            s.At(t0, c =>
            {
                var p = c[who];
                from = p.Root.position + Vector3.up * 1.5f + p.Root.forward * 0.3f;
                to = target(c);
                c.Missile(id, PrimitiveType.Sphere, Vector3.one * 0.22f, new Color(0.78f, 0.66f, 0.45f, 1f));
                DemoContext.Sound("syn_whoosh", 0.4f);
            });
            s.Over(t0, t1, (c, u) =>
            {
                var m = c.Decal(id);
                if (m == null) return;
                m.transform.position = Vector3.Lerp(from, to, u) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * 1.4f);
                if (u >= 1f) c.RemoveDecal(id);
            });
        }

        /// <summary>A bolt flying straight and fast.</summary>
        static void Bolt(DemoScript s, string id, Func<DemoContext, Vector3> from, Func<DemoContext, Vector3> to, float t0, float t1)
        {
            Vector3 a = default, b = default;
            s.At(t0, c =>
            {
                a = from(c);
                b = to(c);
                var m = c.Missile(id, PrimitiveType.Cube, new Vector3(0.06f, 0.06f, 0.7f), new Color(1f, 0.95f, 0.78f, 1f));
                m.transform.rotation = Quaternion.LookRotation(b - a);
                DemoContext.Sound("syn_twang", 0.45f);
            });
            s.Over(t0, t1, (c, u) =>
            {
                var m = c.Decal(id);
                if (m == null) return;
                m.transform.position = Vector3.Lerp(a, b, u);
                if (u >= 1f) c.RemoveDecal(id);
            });
        }

        static Vector3 Chest(Puppet p) => p.Root.position + Vector3.up * 1.15f;

        // ======================================================================================== Pocket Sand
        static DemoScript PocketSand(DemoContext c)
        {
            float blind = c.Def != null ? c.Def.A(c.Rank) : 3f, range = c.Def != null ? c.Def.B(c.Rank) : 8f;
            var s = new DemoScript();
            var callum = c.Spawn("callum", "callum", new Vector2(-1.6f, 0.6f), 90f);
            c.Spawn("thug", "thug", new Vector2(0.3f, 0.6f), -90f);
            c.Spawn("xbow", "crossbowman", new Vector2(3.6f, 3.0f), 225f);
            c.Spawn("sk", "sidekick", new Vector2(-4.4f, -2.5f), 45f);
            ConeHandle cone = null;
            s.At(0f, cx =>
            {
                callum.Flag("combat", true);
                cx["thug"].Flag("combat", true);
                cone = cx.Cone("cone", callum, 120f, 7f);
                cx["xbow"].Play("aim");
                cx.Line("aim", Chest(cx["xbow"]), Chest(callum), Red, 0.04f);
            });
            Duel(s, "callum", "thug", 0.3f, 9.5f);
            s.Caption(0.1f, "He duels in the open. Up on the left, a crossbowman lines up a cheap shot.");
            s.Caption(1.3f, "Out of his sight (outside the pale wedge), you throw:");
            s.Walk("sk", 1.2f, 2.6f, new Vector2(-4.4f, -2.5f), new Vector2(-1.0f, -2.6f));
            s.At(2.65f, cx =>
            {
                cx["sk"].Face(cx["xbow"].Pos);
                cx.Overlay.SkillCallout(cx["sk"], "pocket_sand", 1.4f);
                cx["sk"].Play("throw", 0.6f);
            });
            Throw(s, "pouch", "sk", cx => Chest(cx["xbow"]), 2.85f, 3.35f);
            s.At(3.35f, cx =>
            {
                Vfx.Burst(VfxKind.Sand, Chest(cx["xbow"]), 1.3f);
                cx["xbow"].Play("hit_head", 0.5f);
                cx.RemoveDecal("aim");
                cx.Overlay.Mark(cx["xbow"], "blind", Mathf.Min(blind, 5f), Color.white);
                DemoContext.Sound("cloth", 0.35f);
            });
            s.At(3.9f, cx => cx["xbow"].Play("none"));
            s.Caption(3.45f, $"Blinded for {N(blind)} s, and he never saw a thing. Range {N(range)} m.");
            s.Caption(5.9f, "In his sight, it's a dirty trick:");
            s.Walk("sk", 5.8f, 6.8f, new Vector2(-1.0f, -2.6f), new Vector2(0.5f, -1.4f));
            s.At(6.9f, cx =>
            {
                cx["sk"].Face(cx["thug"].Pos);
                cx["sk"].Play("throw", 0.6f);
            });
            Throw(s, "pouch2", "sk", cx => Chest(cx["thug"]), 7.05f, 7.4f);
            s.At(7.4f, cx =>
            {
                Vfx.Burst(VfxKind.Sand, Chest(cx["thug"]), 1.2f);
                cx["thug"].Play("hit_head", 0.5f);
                cx.Overlay.Mark(cx["thug"], "blind", 3f);
                cone?.View.Flash();
                cx.Overlay.Mark(callum, "alert", 1.6f, new Color(1f, 0.4f, 0.32f));
                callum.Play("scold", 1.6f);
                DemoContext.Sound("ui_error", 0.25f);
            });
            s.Over(7.4f, 8.0f, (cx, u) => cx.Overlay.Bar("honor", callum, Mathf.Lerp(1f, 0.8f, u), HonorGold, 34f));
            s.At(9.1f, cx => cx.Overlay.Mark(callum, "wait", 2f));
            s.Caption(7.5f, "Seen: his Honor falls, and he lowers his sword to wait for the blinded man.");
            return s.End(11f);
        }

        // ======================================================================================== Loosen Bolt
        static DemoScript LoosenBolt(DemoContext c)
        {
            float cap = c.Def != null ? c.Def.A(c.Rank) : 2f, arm = c.Def != null ? c.Def.B(c.Rank) : 1.5f;
            var s = new DemoScript();
            var stack = c.Prop("stack", "barrel_stack", new Vector2(1.4f, 0.8f), 0f);
            c.Spawn("callum", "callum", new Vector2(-5.0f, 1.8f), 90f);
            c.Spawn("thug", "thug", new Vector2(5.4f, 1.2f), -90f);
            c.Spawn("sk", "sidekick", new Vector2(-3.4f, -2.6f), 45f);
            var barrels = new List<(Transform t, Vector3 from, Vector3 to, Quaternion r0)>();
            s.Caption(0.1f, "A marked prop with loose bolts. Kneel beside it:");
            s.Walk("sk", 0.2f, 1.6f, new Vector2(-3.4f, -2.6f), new Vector2(0.4f, -0.1f));
            float armEnd = 1.75f + arm;
            s.At(1.75f, cx =>
            {
                cx["sk"].Face(new Vector2(1.4f, 0.8f));
                cx["sk"].Play("kneel", arm);
                cx.Overlay.SkillCallout(cx["sk"], "loosen_bolt", arm + 0.4f);
                DemoContext.Sound("creak", 0.4f);
            });
            s.Over(1.75f, armEnd - 0.02f, (cx, u) => cx.Overlay.Bar("channel", cx["sk"], u, Cyan));
            s.At(armEnd, cx =>
            {
                cx.Overlay.RemoveBar("channel");
                cx["sk"].Play("none");
                cx.Ring("danger", new Vector2(3.2f, 1.1f), 2.2f, Red);
                DemoContext.Sound("latch", 0.4f);
            });
            s.Caption(armEnd, $"Armed: the red ring is where it lands. Up to {N(cap)} armed at once.");
            s.Walk("thug", armEnd + 0.5f, armEnd + 2.1f, new Vector2(5.4f, 1.2f), new Vector2(3.2f, 1.2f));
            float ping = armEnd + 2.4f;
            s.Caption(armEnd + 0.6f, "Someone walks under it. Ping it:");
            s.At(ping, cx =>
            {
                cx.Overlay.Key(cx["sk"], "ping", 1.6f);
                cx.Overlay.Mark(cx["thug"], "verb_ping", 1.2f, new Color(1f, 0.85f, 0.4f), 26f);
                DemoContext.Sound("click", 0.5f);
            });
            float drop = ping + 0.45f;
            s.At(drop, cx =>
            {
                if (stack == null) return;
                foreach (Transform t in stack.transform)
                    if (t.name == "Barrel")
                    {
                        var to = t.position + new Vector3(1.4f + 0.5f * barrels.Count, 0f, 0.3f - 0.4f * barrels.Count);
                        to.y = DemoStage.Origin.y + 0.45f;
                        barrels.Add((t, t.position, to, t.rotation));
                    }
                DemoContext.Sound("collapse", 0.5f);
            });
            s.Over(drop, drop + 0.55f, (cx, u) =>
            {
                foreach (var (t, from, to, r0) in barrels)
                {
                    if (t == null) continue;
                    var p = Vector3.Lerp(from, to, u);
                    p.y = Mathf.Lerp(from.y, to.y, u * u);
                    t.position = p;
                    t.rotation = r0 * Quaternion.Euler(0f, 0f, -95f * u);
                }
            });
            s.At(drop + 0.55f, cx =>
            {
                Vfx.Burst(VfxKind.Dust, cx.Stage.World(new Vector2(3.2f, 1.1f)) + Vector3.up * 0.3f, 2.2f);
                cx["thug"].Play("hit_heavy");
                cx.RemoveDecal("danger");
                DemoContext.Sound("heavy", 0.5f);
            });
            s.At(drop + 1.0f, cx => cx["thug"].Play("death"));
            s.Caption(drop + 0.6f, "Heavy damage to everyone beneath. Or let Callum lead a bandit under it.");
            s.Caption(drop + 3.0f, "Arming is quiet. If he sees the collapse, it's a dirty trick.");
            return s.End(drop + 5.4f);
        }

        // ========================================================================================= Quiet Feet
        static DemoScript QuietFeet(DemoContext c)
        {
            float heard = c.Def != null ? c.Def.A(c.Rank) : 2f, coneMul = c.Def != null ? c.Def.B(c.Rank) : 0.67f;
            var s = new DemoScript();
            var callum = c.Spawn("callum", "callum", new Vector2(-4.4f, 0.6f), 90f);
            c.Spawn("thug", "thug", new Vector2(2.6f, 1.4f), 0f);
            c.Spawn("sk", "sidekick", new Vector2(-1.8f, -2.6f), 90f);
            ConeHandle cone = null;
            s.At(0f, cx =>
            {
                cone = cx.Cone("cone", callum, 120f, 7f);
                cx.Ring("heard", new Vector2(2.6f, 1.4f), heard, new Color(0.48f, 0.9f, 1f, 0.55f));
            });
            s.Caption(0.1f, KeyGlyphs.Format($"Crouch ({{crouch}}) with Quiet Feet: foes who haven't noticed you can't hear you beyond {N(heard)} m."));
            s.Walk("sk", 0.6f, 4.6f, new Vector2(-1.8f, -2.6f), new Vector2(5.2f, -0.9f), crouch: true);
            s.Over(0.6f, 1.2f, (cx, u) =>
            {
                if (cone == null) return;
                cone.Angle = Mathf.Lerp(120f, 120f * coneMul, u);
                cone.Range = Mathf.Lerp(7f, 7f * 0.6f, u);
            });
            s.Caption(1.6f, $"And while you sneak, his cone narrows to {N(coneMul * 100f)}%: there's less for him to see.");
            s.At(4.7f, cx => cx["sk"].Locomotion(0f, false));
            s.Over(4.7f, 5.1f, (cx, u) =>
            {
                if (cone == null) return;
                cone.Angle = Mathf.Lerp(120f * coneMul, 120f, u);
                cone.Range = Mathf.Lerp(7f * 0.6f, 7f, u);
            });
            s.Caption(4.8f, "Stand up and walk past, and they notice.");
            s.Walk("sk", 5.1f, 6.9f, new Vector2(5.2f, -0.9f), new Vector2(1.6f, -0.3f));
            s.At(5.9f, cx => cx.Overlay.Mark(cx["thug"], "question", 0.9f));
            s.At(6.5f, cx =>
            {
                cx["thug"].Face(cx["sk"].Pos);
                cx["thug"].Flag("combat", true);
                cx.Overlay.Mark(cx["thug"], "alert", 1.8f, new Color(1f, 0.4f, 0.32f));
                DemoContext.Sound("ui_error", 0.2f);
            });
            return s.End(9f);
        }

        // ========================================================================================== Crossbow
        static DemoScript Crossbow(DemoContext c)
        {
            float dmg = c.Def != null ? c.Def.A(c.Rank) : 38f, range = c.Def != null ? c.Def.B(c.Rank) : 22f, reload = c.Def != null ? c.Def.Cooldown(c.Rank) : 3.5f;
            bool pierce = c.Rank >= 2;
            const float shooterHp = 70f;
            var s = new DemoScript();
            c.Prop("perch", "crate_perch", new Vector2(3.4f, 2.2f), 0f);
            var xbow = c.Spawn("xbow", "crossbowman", new Vector2(3.4f, 2.2f), 225f);
            if (xbow != null)
            {
                xbow.Height = 0.95f;
                xbow.Pos = xbow.Pos;
            }
            if (pierce) c.Spawn("thug2", "thug", new Vector2(4.8f, 3.6f), 225f);
            var callum = c.Spawn("callum", "callum", new Vector2(-2.2f, 0.4f), 90f);
            c.Spawn("thug", "thug", new Vector2(-0.4f, 0.4f), -90f);
            c.Spawn("sk", "sidekick", new Vector2(-4.2f, -2.5f), 45f);
            s.At(0f, cx =>
            {
                callum.Flag("combat", true);
                cx["thug"].Flag("combat", true);
                cx.Cone("cone", callum, 120f, 7f);
                cx.Overlay.Bar("hp", cx["xbow"], 1f, HpRed);
                if (pierce) cx.Overlay.Bar("hp2", cx["thug2"], 1f, HpRed);
            });
            Duel(s, "callum", "thug", 0.4f, 8.6f);
            s.Caption(0.1f, "Aim, and loose a bolt (from out of his sight).");
            s.At(0.5f, cx =>
            {
                cx["sk"].Face(cx["xbow"].Pos);
                cx["sk"].Play("aim");
                cx.Line("aim", Chest(cx["sk"]) + Vector3.up * 0.1f, Chest(cx["xbow"]), new Color(1f, 1f, 1f, 0.45f), 0.03f);
                cx.Overlay.SkillCallout(cx["sk"], "crossbow", 1.4f);
            });
            s.At(1.5f, cx =>
            {
                cx.RemoveDecal("aim");
                cx["sk"].Play("shoot", 0.5f);
            });
            Bolt(s, "bolt", cx => Chest(cx["sk"]) + Vector3.up * 0.1f, cx => pierce ? Chest(cx["thug2"]) : Chest(cx["xbow"]), 1.6f, pierce ? 1.95f : 1.82f);
            s.At(1.82f, cx =>
            {
                cx["xbow"].Play("hit");
                Vfx.Burst(VfxKind.Sparks, Chest(cx["xbow"]), 0.6f);
                DemoContext.Sound("thunk", 0.5f);
            });
            s.Over(1.82f, 2.1f, (cx, u) => cx.Overlay.Bar("hp", cx["xbow"], Mathf.Lerp(1f, 1f - dmg / shooterHp, u), HpRed));
            if (pierce)
            {
                s.At(1.95f, cx =>
                {
                    cx["thug2"].Play("hit");
                    DemoContext.Sound("thunk", 0.4f);
                });
                s.Over(1.95f, 2.2f, (cx, u) => cx.Overlay.Bar("hp2", cx["thug2"], Mathf.Lerp(1f, 1f - dmg / shooterHp, u), HpRed));
            }
            s.Caption(1.9f, pierce
                ? $"{N(dmg)} damage, out to {N(range)} m, and at rank II the bolt goes clean through."
                : $"{N(dmg)} damage, out to {N(range)} m. Then a {N(reload)} s reload.");
            s.At(2.0f, cx => cx["sk"].Play("reload", Mathf.Min(reload, 2.5f)));
            s.Over(2.0f, 2.0f + reload - 0.02f, (cx, u) => cx.Overlay.Bar("reload", cx["sk"], u, Cyan));
            s.At(2.0f + reload, cx => cx.Overlay.RemoveBar("reload"));
            float second = Mathf.Max(5.8f, 2.2f + reload);
            s.Caption(second - 1.0f, "A bolt into his duel is a slight, if he sees it:");
            s.Walk("sk", second - 0.9f, second - 0.1f, new Vector2(-4.2f, -2.5f), new Vector2(0.6f, -2.0f));
            s.At(second, cx =>
            {
                cx["sk"].Face(cx["thug"].Pos);
                cx["sk"].Play("shoot", 0.5f);
            });
            Bolt(s, "bolt2", cx => Chest(cx["sk"]) + Vector3.up * 0.1f, cx => Chest(cx["thug"]), second + 0.1f, second + 0.3f);
            s.At(second + 0.3f, cx =>
            {
                cx["thug"].Play("hit");
                DemoContext.Sound("thunk", 0.45f);
                cx.Overlay.Bubble(callum, "Let a man finish his own fight.", 2.6f);
            });
            s.Over(second + 0.3f, second + 0.8f, (cx, u) => cx.Overlay.Bar("honor", callum, Mathf.Lerp(1f, 0.92f, u), HonorGold, 34f));
            return s.End(second + 3.4f);
        }

        // =========================================================================================== Bandage
        static DemoScript Bandage(DemoContext c)
        {
            float heal = c.Def != null ? c.Def.A(c.Rank) : 40f, channel = c.Def != null ? c.Def.B(c.Rank) : 3f;
            const float heroHp = 260f;
            var s = new DemoScript();
            var callum = c.Spawn("callum", "callum", new Vector2(0.2f, 0.6f), 200f);
            c.Spawn("thug", "thug", new Vector2(6.2f, 3.4f), -120f);
            c.Spawn("sk", "sidekick", new Vector2(-4.2f, -2.4f), 60f);
            float hp0 = 0.42f, hp1 = Mathf.Min(1f, hp0 + heal / heroHp);
            s.At(0f, cx =>
            {
                cx.Overlay.Bar("hp", callum, hp0, HpRed);
                cx.Overlay.Mark(callum, "wound_ribs", 6f + channel, new Color(1f, 0.42f, 0.36f), 12f);
            });
            s.Caption(0.1f, "He's hurt, and a cracked rib slows him down. Go to him:");
            s.Walk("sk", 0.2f, 1.6f, new Vector2(-4.2f, -2.4f), new Vector2(-1.1f, 0.0f));
            float done = 1.7f + channel;
            s.At(1.7f, cx =>
            {
                cx["sk"].Face(callum.Pos);
                cx["sk"].Play("bandage", channel);
                cx.Overlay.SkillCallout(cx["sk"], "bandage", 1.4f);
                DemoContext.Sound("cloth", 0.45f);
            });
            s.Caption(1.8f, $"A {N(channel)} s channel beside him. He holds still for it.");
            s.Over(1.7f, done - 0.02f, (cx, u) => cx.Overlay.Bar("channel", cx["sk"], u, Cyan));
            s.At(done, cx =>
            {
                cx.Overlay.RemoveBar("channel");
                cx["sk"].Play("none");
                Vfx.Burst(VfxKind.Heal, callum.Root.position + Vector3.up * 0.8f, 1.1f);
                cx.Overlay.ClearMarks(callum);
                cx.Overlay.Mark(callum, "check", 1.6f, new Color(0.55f, 1f, 0.6f), 12f);
                DemoContext.Sound("ui_confirm", 0.35f);
            });
            s.Over(done, done + 2.6f, (cx, u) => cx.Overlay.Bar("hp", callum, Mathf.Lerp(hp0, hp1, u), HpRed));
            s.Caption(done + 0.1f, $"Heals {N(heal)} over 6 s, and treats a minor wound.");
            float again = done + 2.8f;
            s.At(again, cx =>
            {
                cx["sk"].Play("bandage", channel);
            });
            s.Over(again, again + 1.2f, (cx, u) => cx.Overlay.Bar("channel", cx["sk"], u * (1.2f / channel), Cyan));
            s.Walk("thug", again - 0.6f, again + 1.0f, new Vector2(6.2f, 3.4f), new Vector2(-0.6f, -0.9f));
            s.At(again + 1.2f, cx =>
            {
                cx["thug"].Face(cx["sk"].Pos);
                cx["thug"].Play("attack", 0.5f);
            });
            s.At(again + 1.45f, cx =>
            {
                cx["sk"].Play("hit");
                cx.Overlay.Bar("channel", cx["sk"], 0.3f, new Color(1f, 0.35f, 0.3f));
                DemoContext.Sound("punch", 0.45f);
            });
            s.At(again + 1.9f, cx => cx.Overlay.RemoveBar("channel"));
            s.Caption(again + 1.5f, "A hit (or moving) breaks the channel. The cooldown isn't spent.");
            return s.End(again + 4.2f);
        }

        // ======================================================================================== Cover Story
        static DemoScript CoverStory(DemoContext c)
        {
            float honor = c.Def != null ? c.Def.A(c.Rank) : 12f, window = c.Def != null ? c.Def.B(c.Rank) : 3f;
            var s = new DemoScript();
            var callum = c.Spawn("callum", "callum", new Vector2(1.4f, 0.8f), -100f);
            c.Spawn("thug", "thug", new Vector2(3.4f, 2.8f), -140f);
            c.Spawn("sk", "sidekick", new Vector2(-2.2f, -0.4f), 80f);
            float h0 = 0.45f, h1 = Mathf.Min(1f, h0 + honor / 100f);
            s.At(0f, cx =>
            {
                cx.Overlay.Bar("honor", callum, h0, HonorGold, 34f);
                cx["thug"].Play("hit_head");
                cx.Overlay.Mark(cx["thug"], "blind", 3f);
                callum.Play("scold", 2.2f);
                cx.Overlay.Bubble(callum, "Sand? SAND? Have you no shame?", 2.6f);
            });
            s.Caption(0.1f, "He caught you. Within earshot (14 m), you can talk him round:");
            s.At(2.9f, cx =>
            {
                cx["sk"].Play("talk", 2.2f);
                cx.Overlay.SkillCallout(cx["sk"], "cover_story", 1.4f);
                cx.Overlay.Bubble(cx["sk"], "A rabbit. An enormous, violent rabbit.", 2.6f);
            });
            s.At(5.6f, cx =>
            {
                callum.Play("nod");
                cx.Overlay.Bubble(callum, "Hm. The wind, you say.", 2.4f);
                DemoContext.Sound("ui_confirm", 0.35f);
            });
            s.Over(5.6f, 6.6f, (cx, u) => cx.Overlay.Bar("honor", callum, Mathf.Lerp(h0, h1, u), HonorGold, 34f));
            s.Caption(5.7f, $"Honor +{N(honor)}, and he stops watching you so closely.");
            s.Caption(7.4f, $"Within {N(window)} s of being caught, it also softens the fallout.");
            return s.End(10f);
        }
    }
}
