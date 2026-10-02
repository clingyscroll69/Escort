# QA Report 2 — The skeptical player: is the loop actually fun?

*Perspective: a player who has seen a hundred "escort the dumb NPC" games and assumes this is another one. Every claim
below is backed by a run, a capture or the balance harness. AI bots stood in for human players. That's good for
measuring outcomes, but no substitute for real playtests (see the end).*

## The pitch, as a skeptic hears it

You're the sidekick to Sir Callum, a knight whose virtue is over-held. He challenges, salutes, waits for fallen enemies to
rise, and never looks up at the rooftops. You win by helping him *on his own terms*, ideally where he can't see you.
Escort missions are usually the least fun part of a game, so the question is whether this one is fun *because* of him.

## What works (and why I'd keep playing)

1. **The joke lands in the first 40 seconds.** You walk with your earbuds in and your phone up, a cheerful song building
   to its chorus. A horn rises out of the muffled street, headlights flood in, and the song cuts on the drop that never
   comes (`shots/open3_07_real16.png`). White. Then the status window rolls CLASS: … Barista, Tax Auditor, Dark Lord… HERO
   ("YES. I finally get to be the hero."), and red glyphs settle on **HERO's SIDEKICK** (`shots/open3_14_real34.png`). On
   the road he introduces himself ("Callum, of the Code. Keep up, sidekick, and fight fair."), and your label quietly
   becomes CALLUM's SIDEKICK (`shots/meet_05_real03.png`). The walk is 16 seconds of watching, which is fine once: replays
   get a 3-second version, and skipping takes two presses so a stray click can't eat the joke.
2. **His rules are a readable puzzle, not stupidity.** A rule icon over his head (sword, hourglass, shield) and a visible
   witness cone on the ground tell you what he'll do next and what he'll see. The best moments come straight out of those
   rules:
   - **The fake surrender.** A turncoat "yields" and Callum lowers his guard; you have about 2.4 s to blind him before the
     knife comes out. The bots that did this kept Callum alive; the ones that didn't watched him die
     (`logs/watch_ch1_rap.log`: S0 Callum killed by the cheap shot in Room 0).
   - **Scouting during his threshold pause.** He stops at every doorway. That's your window to flush the hedge ambush
     while he's 20 m away and can't see you. I changed the judge so this pre-emptive play actually earns credit; it didn't
     before.
   - **The rigged duel.** Formal terms, "Do you hear the gallery?", "Now!", then volleys from archers he will never look
     at. Break the watching stone, then pick off the gallery from behind his back. At S1 it's a thriller: alone he loses
     with Ashgrave on **2 HP**, and silencing one archer wins it (`RiggedDuelTests.Balance_Report`).
3. **Stage 0 vs Stage 1 is visible, not a number.** At S1 he waits 2 s instead of 3 and spares the turncoat *before* the
   stab. He raises his guard after the first volley ("Archers! Then I'll fight with my guard up"). The camp warms up: a
   bigger fire, he faces you, "You were… useful today. I noticed." (`final2_h2_23_t083.png`), against the cold "We camp
   here. I'll take the first watch." with his back turned (`flow2_22_camp.png`). His win line becomes "Hm. We."
4. **Losses are explained, not just suffered.** The Curator's diagnosis names the flaw ("He agreed to my terms… never once
   looked up at the gallery… I have no page for you"). The Post-Mortem lists what you did and what he faced alone, in
   words, never numbers (`flow2_27_end.png`). A loss reads as "I know what to try next", which is the right feeling.

## What doesn't work yet (where a skeptic quits)

1. **"I kept him alive and the game graded me S0."** This is the biggest risk. The most *helpful* bot (sand + bandage)
   reached the duel in **10/10** runs and passed **100%** of rooms, yet earned S1 in **0/10**. Its help was witnessed: Callum
   saw it sand a yielded man, which costs Honor and Rapport even though it saved his life. Only the Cover Story build reached
   S1 reliably (**4/10**, and won every one of those duels). The design intends this ("help him where he can't see"), but
   right now the game teaches it only through penalties. A first-time player will feel punished for being heroic.
2. **The S0 duel is effectively a wall.** Across the final batch's 27 S0 duels the bots won **zero** (all 5 S1 duels were won). The GDD says the S0 boss should be
   "effectively closed", but in a 20-minute slice it means most first runs end in an unwinnable fight. The Restore Point is
   one click away, but only if the player understands *why* they lost (see 1).
3. **Casual play dies on the road.** The sloppy bot (random skills, never aims) reached the camp **2/10** times. The deaths
   are fair and explained (traps in the Ruined Gatehouse, the Wagon Camp's four bandits plus a cheat), but the slice is
   hard for someone who hasn't learned Callum's code.
4. **You watch more than you do during his duels.** With help in his cone penalised, the "right" play is often to wait for
   a quiet angle. Knifing flankers is allowed (he only minds his own duel) and fills the gap, but nothing tells you that.
5. **It's short and light on juice.** A full run takes about 2.5 minutes at bot speed (GDD target for Ch1: 8–10 min). Hits
   have sparks and sounds but no hit-stop, and the sand cloud and brute heavies could land harder. The audio I added
   (CC0 Kenney + self-made) covers combat, UI and story beats; there's no composed music yet.

## What I changed because of this review

| Problem a player would feel | Change |
|---|---|
| Four bandits deleting the hero in 12 s (solo rate 22%) | Melee attack tokens: two swing, the rest circle; solo rate now **65%** (GDD ~70%) |
| Callum flip-flopping or politely waiting after his own hits | Committed fall-back; his own blows never count as "unready" |
| Lines that explain his behaviour drowned out by chatter | Bark tiers: rule-explaining lines always speak; chatter is paced |
| "I can't tell what he sees" | Visible witness cone (stronger while you sneak or aim) and a visible stone view |
| Arrows from nowhere; the hero leaving the screen | Off-screen threat chevrons, camera leash, hero edge marker |
| Smart scouting not rewarded; Moments you could never capture counted against you | Earlier ambush offers; uncapturable offers withdrawn |
| Death by a thousand cuts between rooms | Second wind on reaching the next room (wounds still persist) |
| Bandages never finishing | He holds still while you dress the wound |
| Silence | A complete, if sparse, sound layer |

## Recommendations (in order)

1. **Teach the code in Room 0.** Run a scripted first encounter: show the cone ("he sees 120°"), the wait ("he won't strike
   the unready"), and a fake surrender he falls for. Turn Hero Insight on for the first run.
2. **Make the camp explain the Stage in words**, without numbers. Cold S0: "Someone threw sand at a man who had yielded. I
   don't know what to make of you." Warm S1: "Strange luck today. The archer fell before I turned." (Owner-written copy.)
3. **Soften the penalty for life-saving witnessed help.** For example, halve *Caught* when the deed averted an active cheat.
   Then re-run the harness until at least two builds reach S1 in ≥30% of runs.
4. **Give S0 duels a learnable route**, such as the broken stone revealing the perches, or Quiet Feet showing archer glints
   from further away. Keep it hard, but legible.
5. **Add juice:** hit-stop on a riposte, camera shake on a brute's heavy, a denser sand cloud, footsteps. Commission or license
   music.
6. **Write the barks.** The premise lives in Callum's lines; the drafts are placeholders.

## Bottom line

The loop has a real hook: a hero whose rules you learn, exploit and quietly cover for, with a visible reward when he
starts to trust you. That's rarer than a good escort mission. The current build makes the *right* way to help too hard to
discover, so the first impression is "the game punishes me for saving him." Fix the teaching (1, 2) and the penalty
tuning (3) before showing it to playtesters. The GDD's own gates (retell a moment, tell S0 from S1, loss feels attributable)
need humans to confirm, and the evidence above says they're within reach.
