# Third-party assets

All third-party art and audio in this project is **CC0 1.0 (public domain)** — 3D by Quaternius (https://quaternius.com,
official itch.io pages at $0 "pay what you want") and sound effects/jingles by Kenney (https://kenney.nl). The one font
is SIL Open Font License 1.1 (below). No attribution is required for CC0; credited here anyway.

| Kit | Source | Used for |
|---|---|---|
| Universal Base Characters [Standard] | https://quaternius.itch.io/universal-base-characters | Heads, eyes, brows, hairstyles (grafted in Blender) |
| Modular Character Outfits – Fantasy [Standard] | https://quaternius.itch.io/modular-character-outfits-fantasy | Peasant / Ranger outfits (recoloured) |
| Universal Animation Library [Standard] | https://quaternius.itch.io/universal-animation-library | 43 humanoid clips (locomotion, combat, emotes) |
| Universal Animation Library 2 [Standard] | https://quaternius.itch.io/universal-animation-library-2 | 43 humanoid clips (sword set, throw, shield, consume) |
| Fantasy Props MegaKit [Standard] | https://quaternius.itch.io/fantasy-props-megakit | Sword, shield, table knife, axe, bucket, camp props |
| Stylized Nature MegaKit [Standard] | https://quaternius.itch.io/stylized-nature-megakit | Trees, rocks, grass, paths |
| Medieval Village MegaKit [Standard] | https://quaternius.itch.io/medieval-village-megakit | Walls, arches, fences, wagon, stairs |

| RPG Audio | https://kenney.nl/assets/rpg-audio (CC0) | Knife, cloth, unsheath, metal click/latch, creak, chop |
| Impact Sounds | https://kenney.nl/assets/impact-sounds (CC0) | Sword/metal/punch/wood impacts, thuds, the tripwire bell, the opening's footsteps (mixed into `syn_open_street`) |
| Interface Sounds | https://kenney.nl/assets/interface-sounds (CC0) | UI select/confirm/error/open/tick, stone chime, the opening's glitches and "bong" |
| Music Jingles | https://kenney.nl/assets/music-jingles (CC0) | Room-clear, camp, duel, win and loss stingers |
| Share Tech Mono (font) | https://github.com/google/fonts/tree/main/ofl/sharetechmono — **SIL OFL 1.1**, © Carrois Type Design; licence text in `Art/Fonts/ShareTechMono-OFL.txt` | System window, buttons, titles |

TextMeshPro's LiberationSans (bubbles, HUD) ships with Unity's TextMeshPro package.

Project-made (tools/blender/make_props.py, procedural bpy): Crossbow, Club, Dagger, Pendant, ChronicleStone, BandageRoll.
Project-made audio (tools/make_sfx.py, numpy synthesis — self-made per GDD §9): whooshes, crossbow twang, horn, fire and
forest loops, duel and camp pads. The opening (tools/make_opening.py): the song "Main Character" (a 16 s synth-pop
composition written for the game), the muffled street, the truck, the replay sting and the ringing. Picked clips live in `Resources/Audio` (originals unpacked in `ThirdParty/Kenney`).
Recolours and head grafts: tools/blender/build_characters.py + tools/recolor_textures.py.
No AI-generated meshes, textures or audio are used (the synthesized audio is procedural code, not generative AI). Bark/dialogue text is AI-drafted placeholder copy for the owner to
rewrite (GDD §8: shipped AI-written text likely needs a Steam disclosure).
