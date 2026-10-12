# Natural Ocean

A sea of its own for every world that has one: waves raised by the wind the weather has, swell from elsewhere,
white water where they break, surf where the bottom shelves, seaweed in the shallows, and water that a camera
can go half under.

**A first sea, not a finished one (0.1).** From a boat's deck in a fresh wind it reads as a sea at a glance; it does
not yet stand beside a photograph of one. What is wrong with it is said below, and at length in
[the plan](docs/Plan.md), which is kept true as the work goes; what real seas do, and how
they are drawn, is in [the research](docs/Research.md).

![The sea as it stands, in ten pictures: a light air; a fresh wind from 80 m and from 30 m up; a whole gale from the air and from a boat's deck; the coast and its surf; a swell breaking on the beach; under water; looking up from 9 m down; and towards a low sun](src/NaturalOcean/img/ocean-tour.jpg)

*Each picture a scene set afresh, the wind held, the camera held in its place for forty seconds first so that the
foam has its history.*

For Kerbal Space Program 1.12.x. Needs [Keystone](https://github.com/IshiakiZ/ksp-keystone), a separate download ([its page on SpaceDock](https://spacedock.info/mod/4665/Keystone)): this mod does not load without it. With
[Natural Weather](https://github.com/IshiakiZ/ksp-natural-weather) installed the sea goes by the weather's wind; without it, by a
light breeze.

## What it does

* **Waves from the wind**, as the sea's measured spectrum has them: every length from a storm's swell to ripples,
  running at the speeds water gives them, higher and longer the harder it blows, with swell from elsewhere.
* **White water with a memory**: a crest breaks where it grows too steep, the breaking runs on with it and stops, and
  the foam it leaves lies and fades; in a gale it is drawn out in rows down the wind. In a storm, now and then, a
  crest goes right over.
* **The shore**: surf that comes in along the beach, stands up, breaks white and runs up the sand; green water over
  the shallows, and seaweed on the bottom.
* **What floats rides the waves** that are drawn, and leaves foam where it churns the water.
* **A camera that can go half under**: the waterline across the glass exactly where the drawn sea cuts it, the water
  dimmer and bluer with depth, the sun's light gathered by the waves.
* **Beside the other seas**: where this sea is drawn the game's own is not, and Scatterer's is drawn with nothing of
  it showing (Scatterer's settings are never touched).

## Install

Install [Keystone](https://github.com/IshiakiZ/ksp-keystone) first: it is a download of its own, and its `Keystone` folder goes into your KSP
`GameData`. Then copy the `NaturalOcean` folder from this mod's download into `GameData` beside it.

## Its settings

In the Keystone window, on the page Natural Ocean:

* **Natural Ocean**: off, the sea as the game (or another mod) draws it.
* **How rough**: the wind the sea is given, against the wind the weather has (0 a flat calm; 2 twice the wind).
* **How clear the water is**: how far down the bottom can be seen.
* **Craft ride the waves**: what floats is lifted and dropped by the waves that are drawn.
* **Seaweed**: grass and weed on the bottom where the water is one to eleven metres deep.
* **Put other seas out of sight**: where this sea is drawn the game's own is not, and Scatterer's is drawn
  with nothing of it showing.

## Good to know: what is wrong with it

* **A blowing sea still looks tame from a boat's deck.** A breaking crest is white laid on a smooth wave: its roller
  is a moving pattern, not water going over. In a storm the few crests that curl do so only near the camera, and seen
  from inside or below a curl is flat slabs; from far off it is blocky. Nothing happens where two seas cross.
* **Fresh foam is too clean**: a flat white sheet with a sharp edge. Spray is hard to see, and nothing sprays where
  surf falls; the sand is not wet where a wave has been.
* **The water is one blue**: no colour from depth or from what is in it, no wind lanes or slicks.
* **From the air the sea mirrors a plain sky**, not the clouds.
* **What floats leaves only foam**: no bow wave, no wake of waves, no ring from a splash.
* **The sea is as rough in a sheltered bay as on the open ocean** (a long fetch is assumed everywhere), and other
  worlds' seas are drawn with Kerbin's water.
* **Beside other mods that draw the sea** (Scatterer's own ocean left showing, Parallax's, EVE's) it has not been
  tried. Beside Scatterer with this mod's "Put other seas out of sight" on, it has, on a Mac.
* It costs about half a processor core and little of the graphics card on the Mac it was made on; on Windows it ran
  at 58 to 109 frames a second (see below). It has not been set against a game without it.

## Where it works

| System | |
| --- | --- |
| Mac | Made and looked at there (KSP 1.12.5, OpenGL, with Scatterer installed): see the plan. |
| Windows | Tried on Windows 11 (KSP 1.12.5, Direct3D 11, an NVIDIA GeForce RTX 4080 SUPER, 1280 x 720; without Scatterer, with TUFX and Natural Weather) on 2026-10-11, in a development build that held the wind, its four shaders built for Direct3D that day (`PluginData/ocean-windows.bundle` and the three beside it). Seen there, each beside the same view on OpenGL on the same computer (the game started with `-force-glcore`) and alike: **from the shore** east of the launch pad in a light wind, green water over the sand shading to deep blue, weed on the bottom seen through it, a thin white line of surf at the water's edge; **a breeze** held at 9 m/s, a dark blue sea of waves under two metres with a few whitecaps, and at the beach a wave standing up, curling over and running in white; **a storm** held at 26 m/s, high seas streaked and patched white, crests breaking, spray off them; **the camera half under water**, a clean line across the glass where the sea cuts it, the sky above and the underside of the waves below; from under water, the rippled underside with its bright window to the sun, and weed on the bottom; **a capsule let down into it** under its parachute: drops thrown up in arcs as it went in, a sheet of white water round it, then the capsule afloat and riding the waves while the foam thinned to lace. All of it again with TUFX taken out (the game then draws straight to the screen, with smoothed edges, and its picture cannot carry the numbers the lens goes by): alike, but that seen from under water the weed on the bottom was faint. Checked by numbers, both ways: white water laid at four known places was read back at those places (so the spray rises where the water breaks), and the lens's map of the water's height round the glass, read back, lay as the lens reads it and not mirrored. On the release builds the sea loaded without an exception and was seen from a rocket climbing from the pad. Later that evening, with the storm sea's crests changed on the Mac (they spill, with a roller), its shader was built for Direct3D again and seven of those looks taken again on Direct3D 11 and on OpenGL: alike. The storm at 26 m/s was then filmed on Direct3D 11 (8 s from 8 m off, 8 s from close by at twelve frames a second, 12 s from 80 m up): the crests that break spill, white water lying on the forward face and drawn out down it in ragged tongues from one quarter of a second to the next, with spray off it; none of them curled (in the 12 s from the air, taken in the picture that shows the curl alone, nothing was marked). With a curl forced on every steep crest (the development build's tuning) the lips were drawn. That look in motion was on Direct3D only, not beside OpenGL. Four faults were found on the way and mended (see the plan): three lines of error in the log at every start, on every system (the seaweed asked the sea's material for the mirror's values before it had any); without Scatterer the game's own sea was drawn through this one wherever this one stood lower; on Direct3D the clouds were laid over the nearest waves and the underside; on OpenGL with NVIDIA's driver the sea lay flat as glass. One thing seen on both Direct3D and OpenGL and left: near the beach in a light wind, a pale mound of raised water with a hard edge. Not looked at there: other worlds' seas, Scatterer's sea beside it, what it costs. |
| Linux | **Never run.** It uses the same OpenGL shaders as the Mac. With NVIDIA's driver the sea would have lain flat as glass before 2026-10-11, as on Windows on OpenGL. |

More: [the plan](docs/Plan.md), [the research](docs/Research.md).

## Licence

[CC BY 4.0](LICENSE): use it, change it and pass it on as you like, as long as you credit Ishiaki and link back here.
