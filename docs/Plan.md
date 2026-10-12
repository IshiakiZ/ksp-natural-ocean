# Natural Ocean: the plan, and where it stands

Asked for on 2026-10-11: "I want the oceans to look as beautiful as possible. I want them to look as realistic as
possible. Also, if you have the weather mod, I would like it to be compatible with it, and based on the weather, have the
oceans change to be really rocky seas and realistic ... white water bubbles; mist from waves crashing into each other;
oceans or waves that curve over themselves; basically everything that makes it realistic; seaweed and all the stuff".

What real seas do and how they are drawn is in [Research](Research.md). This page says what is made, how, what is
still wrong with it, and what comes next. It is kept true as the work goes.

## Where it stands (0.1.0, 2026-10-11, 14:55: not released; a sheet of ten pictures sent to the user at 13:17, and four of the half-under view with a film at 14:45)

**It is a first sea, not a finished one.** From a boat's deck in a fresh wind it reads as a sea at a glance: waves of
every length from the swell to ripples, white horses that come with the wind and leave foam behind them. It does not
yet stand beside a photograph: see "What is wrong" below.

| Stage | What | State |
|---|---|---|
| 1 | The surface: waves from the spectrum measured at sea | made |
| 2 | The light on it | made: the real sky mirrored (clouds, land), the sun's path in points of light |
| 3 | White water: foam that is remembered | remade (12:40): breakers that begin, run with their crest and stop, each leaving a patch that fades in three stages; rows down the wind in a gale; in the measured amounts. Spray off the crests is plain puffs still |
| 4 | Weather: the wind raises the sea | made as far as wind and shade; rain, gusts, fetch not |
| 5 | The shore: shoaling, breakers that curl over, swash | first cut: surf along the lines of equal time to shore, curling over as it breaks, running up the sand; no spray from it, no wet sand |
| 6 | What floats: craft lifted by the waves, wakes, splashes | craft ride the waves (a test rocket follows four fifths of the water's rise and fall); first cut (15:35) of the mark they leave: foam where a hull churns the water, which lies and fades as all foam does (so a wake behind what moves), and at a splashdown a wider patch and spray thrown up. No waves made by a hull yet |
| 7 | Under water | made (14:55): the lens in the water. Half under and half over as a real camera has it, the edge exactly where the drawn sea cuts the glass; water between the glass and things, its light dimmer and bluer with depth; the sun's light gathered by the waves on things and as shafts; the surface from below (window of sky, sun, mirror). The seabed itself is dark (the game's own colour for it) |
| 8 | What grows: seaweed | first cut: beds of grass on the shallow bottom, plants near the camera, dark patches from afar |
| 9 | Other worlds' seas (Laythe, Eve) | drawn with Kerbin's water; their own not begun |
| 10 | Cost, and Windows | half a processor core and little of the graphics card on the Mac; on Windows (Direct3D 11) its four shaders are built and the sea was seen on 2026-10-11: see "On Windows" below |

## How it is made

### The waves (`Waves.cs`, `Spectrum.cs`, `Fourier.cs`)

Tessendorf's method: the sea is the sum of thousands of plain waves, each with the height the measured spectrum gives
its length and direction and a random start, each running at the speed water gives a wave of that length; the sum is
taken with a Fourier transform, on the processor (two threads of the mod's own), every frame.

- **Eight patches**, 1800 m across down to 0.28 m, each 3.5 times smaller than the last and 64 points each way, each
  carrying the waves that fit 3 to 10.5 times across it: every wave has six points or more to its length, and there is
  detail from a 600 m storm swell to ripples 3 cm long. (The first version had four patches of 128 points with bands
  seven times as wide: they stood nine tenths empty, cost three times as much, and their shortest waves were 9 cm.)
- **Every patch is laid on the sea twice**, the second time four fifths of the size and turned eleven degrees, and the two
  are added. One patch alone repeats, and from the air its repeat showed as a grid of identical whitecaps 37 m apart; two
  of different sizes turned against each other never come round to the same pattern.
- **The spectrum** is JONSWAP's (a sea still growing over its fetch; Pierson and Moskowitz's when fully grown), with the
  depth correction for shallow water, Mitsuyasu's spread of directions (not below a spread of 2.2: Cox and Munk found
  slopes half as large again along the wind as across it, and a wider spread made the ripples a crinkle with no
  direction), and a swell from elsewhere as a narrow band. Ripples swing by the water's skin as well as by gravity.
- **The short waves are set by measure, not by formula.** Cox and Munk (1954) measured the sea's slopes from the sun's
  glitter: 0.003 + 0.00512 U in the mean square on clean water, and 0.008 + 0.00156 U with the ripples killed by an oil
  slick (waves longer than a foot). When the sea is shaped, the stretch of the spectrum that falls as the fourth power is
  made as long as gives the second number, and the two smallest patches are multiplied by what brings the whole to the
  first. The sea's report (`NaturalOcean.Sea.Say` in the development build) prints both.
- **Crests are sharpened** by moving the water sideways towards them (the "choppy" displacement), and each patch gives
  how squeezed the water is: squeezed to nothing and past it is a crest folding over, which is where foam begins.
- **Cost**: 6 to 7 ms a frame on two threads, about half a processor core while the sea is in sight (the game alone used
  1.7 cores in the same scene; the first version's sea cost 2 more).

### The mesh (`Surface.cs`) and the shader (`tools/shaderpack/ocean.glsl`)

Ten square rings of 128 cells round the point of the sea under the camera, each ring's cells twice the last one's, slid
with the camera in whole cells so nothing swims; the innermost cell follows the camera's height (an eighth of a metre
from a boat, 64 m from high up); the outermost rim is pushed past the horizon and the lot bent over the planet's curve.

The light: Fresnel's mirror, lessened for a rough sea seen along (Bruneton's fit); the water's own colour from light
scattered back out; the bottom seen through shallow water, the red taken first; a green glow through thin crests against
the sun. Each patch carries the squares of its slopes beside its slopes, so what smoothing takes away at a distance comes
back as the right roughness (LEAN mapping): the sun's path is as wide as the whole sea's slopes at any distance.

**The sky in the water is the real one**: the picture the game finished last frame (kept half size with smaller copies,
taken before the lens has it) is read where a mirrored ray would have been in it, as blurred as the sea is rough there.
So clouds, land, a flame are in the water; this frame's own picture cannot be had, because the clouds are drawn after the
sea. Where the mirrored ray leaves the picture (looking down from the air, nearly all of them do) a plain sky of two
colours stands in, made to meet the real one at the horizon where the horizon is in view. Still wanted: a small picture
of the whole sky with its clouds, from the weather mod, so that the sea seen from above mirrors clouds too.

**The sun's path is points of light**, as many as there really are (Longuet-Higgins: the facets of the right tilt times
how sharply the ripples curve), dealt out by Poisson's odds to patches of water the size of a pixel, pinned to the water
and changing twenty times a second. A fifteenth of the true count is used: a pixel of the picture is five times as wide as
what an eye can tell apart, and with the true count the path was a dull sheen where an eye sees sparkle. Its brightness
is the physical one (in the game's numbers a white thing in the sun is 1: the facets' mirror of the sun is pi times
their share): about as bright as the sky with the sun high, far over white towards a low sun.

### The shore (`Seabed.cs`)

Four maps of how high the ground stands against the sea's level (2 m to 128 m between points, 256 m to 16 km across),
kept as the foam's maps are; the game is asked for its terrain's height a few hundred points a frame (7 millionths of a
second each). Each patch's waves are kept only as far as the depth allows (0.45 of the depth for the mean of the highest
third, as measured in surf), in the mesh, in the light and in the foam alike, so at the water's edge nothing is left of
any. Where the bottom has taken waves the water breaks more readily, and the last half metre of depth is white whatever
the waves do.

**The surf.** Out at sea waves run every way; over a shelving bottom they slow (to the root of g times the depth) and
their crests swing round to lie along the shore. So a second picture has, for every point, how long a wave takes from
there to the shore (the eikonal equation, by fast sweeping: one sweep of one map a frame while anything changes), and the
lines of equal time ARE the lines the crests lie on. The surf is two trains of waves (the swell's and the wind sea's)
laid along those lines: each wave of a train its own height (sets), growing as the water shoals (Green's law), its front
steepening, breaking where it is 0.78 of the depth high and running on as a white bore; at the water's edge each
arriving wave lifts the level, which is the sheet that runs up the sand and drains back. A slow pattern pinned to the
water puts the count a little forward or back, so the shore is not struck all along at once. The surf goes by the depth
"as a wave feels it" (a third picture: the depth taken over ten metres round), because the game's seabed is all bumps a
few metres across and a wave ten times as long does not notice them: by the true depth, every bump raised a ring of
standing water with foam on it. The same code is in the sea's shader and in the foam's pass, word for word
(`glcheck_all.py` sees that it stays so).

**The curl.** A wave that breaks by plunging does it so: the water just behind the top of the crest outruns the wave,
goes over the front and falls. Where a surf wave is breaking the mesh is made to do the same: the points a little behind
the crest are carried towards the shore (the way the time to the shore falls fastest, taken over three metres to either
side, and only where it falls about as fast as a wave runs) past the wave's own front, and down, so that a lip hangs
over the face. Only where the mesh is fine (within some thirty metres of a low camera); further off the wave breaks
white without it. A first cut: the lip is there and reads as a breaking wave from the beach, but the wave is a smooth
lump with foam painted on it, not tumbling water, and nothing sprays from it. Lone rocks awash no longer count as shore
(each drew a ring of surf in round itself).

The game's own coasts shelve very steeply (east of the space centre the water is 5 m deep 15 to 100 m from the edge), so
its surf is a shore break a few metres wide, not lines of rollers; where a mod or another planet has a gently shelving
beach there will be more of it.

### The white water (`Foam.cs`, `tools/shaderpack/oceanfoam.glsl`): remade on 2026-10-11, 12:40

Kept by the graphics card and remembered **on the water**, not in space: one picture of four maps round the camera, each
with points four times as far apart as the last (an eighth of a metre to eight metres from a boat; more from higher),
and beside it a second picture of the same four maps that holds where breakers' **fronts** are running.

**A whitecap is an event.** Each size of wave (each patch of the waves, with its second laying) breaks by itself:

1. It **begins** where that size's own crest is squeezed past a level of its own (with half the squeeze of the longer
   waves counted in: short waves break on the crests of long ones). That is rare: a spot.
2. From there the front **runs**. Every point of that crest's steep stretch (squeezed to within 0.2 of the level) that
   lies beside a point where the front already is, joins it: down the wind at once (the crest carries the front with
   it), along the crest and back at half the wave's speed. The white is laid only at the crest and a little way down its
   forward face (a band about a thirtieth of the wave's length deep).
3. After half the wave's period the front is **over**, wherever it has got to.

So a whitecap is a bright curved front that runs with its crest for a second or two and the patch it has swept, paling
behind it: some metres across in a breeze, tens of metres in a gale. Which sizes of wave make the white goes by the
wind (most of the breaking crest runs at a third to a half of the dominant waves' speed: Phillips; Kleiss and Melville);
the dominant waves themselves do not break. A map too coarse to hold a size's fronts (points further apart than a sixth
of its length) has that size's white as specks, four times as many as are breaking, to make up what sweeping would leave.

What a front leaves **fades in three stages**: half a second of dense froth (the bright part), five seconds of froth
falling in and opening into lace, eight of a thin skin of bubbles drawn into strings; and it spreads a little as it lies,
so an old patch's edge is soft. In a wind the surface water runs sideways into **rows** laid down the wind (the rolls
the wind sets up in the top of the sea, Langmuir's): the white is carried with it, a hand's breadth a second, and gathers
in the rows, which is what the streaks of a gale are; the last thin foam lasts four times as long in a gale as in light
airs. The rows are a slow pattern pinned to the water, eight and thirty-two metres apart, made to wander.

The sea's shader draws the white by how fresh it is: froth standing up from the water (its edges and the lips of its
holes take the light or lose it), a lace of holes of all sizes pulled about so that they do not stand in rows, torn at a
patch's edge; and the water milky green round where a breaker drove its bubbles under.

**What it took to get here** (each was looked at in the game and thrown out): all the sizes' squeezes added and held
against one level (the water broke where crests of different sizes happened to cross, for a tenth of a second: round
flecks that flashed); those drawn out along the wind's cross-line (rectangles); one level a size without the running
front (spots again: the most squeezed thousandth of a field of waves is spots, whatever the field); the front given a
second and a half of dense froth (a whitecap was a sheet of snow twenty metres deep); the rows gathering foam ten times
too fast and exactly evenly apart (a gale's sea was corduroy).

Measured in the game (`Say`, while the camera is held over the sea) against photographs of the sea:

| Wind | Bright (breaking and just broken) | White of any kind | From photographs |
|---|---|---|---|
| 9 m/s | 0.1 % | 0.7 to 1.4 % | 0.5 % (Callaghan), 0.7 % (Monahan and O'Muircheartaigh) |
| 15 m/s | 0.4 to 0.6 % | 2.2 to 3.3 % | 2.4 %, 3.9 %; breaking at a moment 0.17 % (Monahan and Woolf) |
| 22 m/s | 1.2 to 2 % | 8 to 12 % | 6.6 %, 14.6 % |

### Spray, streaks and the air (`Spray.cs`, and in the sea's shader)

- **Spray**: every frame some hundreds of places on the water near the camera are looked at, at random; wherever the
  waves' numbers say the water is well past breaking, a burst of small soft puffs is let go along the crest there, blown
  down the wind and drawn out along the way each moves (the game's own smoke shader, lit as the foam is). None below
  9 m/s, all of it from 18. (Natural Light, which tints the game's smoke by the light of the hour, took these puffs for
  smoke and made them orange in the afternoon: it now leaves a material named "Natural Ocean..." alone.)
- **Streaks of foam down the wind** are no longer drawn as a pattern (two tries: a ploughed field, then lines like ropes
  to the horizon): they are what the foam does when the wind's rolls gather it (see the white water above).
- **The air between** the eye and the far sea pales it as the land is paled, by how much air the line of sight meets
  (thinning upwards), with the horizon's own light from last frame's picture; in a gale spray thickens the air near the
  water.
- **From under water** the surface is a window of sky 48 degrees wide with the sun in it and a dark mirror outside it;
  the game's own underwater fog goes by the water's height at the camera.

### Seaweed (`Weed.cs`, `tools/shaderpack/oceanweed.glsl`)

Grass and weed grow on the bottom where the water is one to eleven metres deep, in beds. Where the beds are is a matter
of the place on the planet (a slow pattern worked out from the point's own position in the planet's frame, the same
every time the game is played). Near the camera the plants are there: the seabed in squares of sixteen metres by
latitude and longitude, one mesh a square (one square made a frame; the game is asked the depth at each plant), the
terrain's child. A plant is seven thin ribbons laid out and swayed by the shader; the mesh only says where each plant
stands, and what differs from plant to plant rides in the last digits of that position (the shaders packed by hand are
given positions and nothing else; which corner of which ribbon a point is, is read from its number in the mesh). Further
off, and from the air, the beds are dark patches in the pale water over sand: the sea's shader darkens the bottom it sees
by the same pattern. A first seagrass: one kind of plant, plain ribbons, no kelp, no weed afloat, none cast up on the
beach.

### What floats (`Floating.cs`)

The game floats every part by its own reckoning, against a water level that each part carries (nought: a flat sea).
Before each step of the game's physics every part of every craft within a hundred metres of the sea is told how high
the water stands where it is, from the same numbers the picture is made from, and the game's buoyancy does the rest.

Two things had to be added, both found by measuring (a test rocket in a 3 m sea followed 4 % of the water's rise and
fall and was at times three metres under) and by reading the steps of the game's own buoyancy (`Sea.Il` in the
development build lists them):

- **The game holds a floating part to rest, not to the water.** A part in water moving slower than 1.6 m/s has its speed
  damped at a rate of 2 a second, as if the water stood still; but a wave's water is itself moving, a metre a second and
  more, up as the crest comes and down behind it. Each part is given the push that makes the game's damping pull it
  towards the water's own speed: the damping's rate times the water's speed (which is worked out from two successive
  states of the waves). A faster part the game hardly damps at all; up and down only, and only for what is not travelling
  fast over the water (boats under way and seaplanes are left to the game), it is held to the water at the slow rate too.
- **Coming back into the water is not a splashdown.** To the game a part entering the water is one: dragged thirty times
  as hard for three seconds, and broken if it comes in fast. In waves a floating thing's parts leave the water and come
  back with every crest. A part that was in the water within the last five seconds is taken as never having left; what
  comes down out of the air is a splashdown as before.

None of the game's physics settings is changed, and with the mod's "Craft ride the waves" off everything is as the game
has it. With it on the rocket follows 78 % of the water's rise and fall, in step (0.9), and the game still lets it be saved
and recovered while it bobs. Not yet: the surf's lift near a beach, wakes, splashes, kerbals tried swimming, big craft
timed (each part costs a few millionths of a second a step).

### The other seas (`Sea.cs`, `OtherSeas`)

The game's own sea is switched off where ours is drawn. Scatterer's is drawn with nothing of it showing (two numbers its
own material has for fading the sea out from orbit, set just before each camera draws); Scatterer's settings are never
touched. Scatterer's wave physics (on Windows) is not looked into.

Without Scatterer installed (tried on the Windows PC, 2026-10-11) the game's own sea was drawn through ours wherever
ours stood lower: sheets of it along the swell, edged white, because our sea took it for a bottom a hand's breadth
down. Its pieces had been looked for among the things under the sea's own object and their renderers switched off
once a second; but the pieces the game makes as the camera moves are kept under another object ("PQStorage"), and
a piece switched off is switched on again by the game when it next comes into view. Now every piece is found as the
game itself keeps them (the tree of squares, `PQS.quads` and each one's `subNodes`), and its renderer is told not to
draw (`forceRenderingOff`, which the game never touches), as each camera that could see it sets out. The game keeps
its pieces in one store for every sphere, and a piece that leaves the sea may be the land's next: what is kept from
being drawn is remembered, and whatever is no longer a piece of the sea is let go at once (`OtherSeas.Put`).

While the lens's own pass is there (next), every whole-picture view of being under water is kept off, since each goes by
where the MIDDLE of the camera is against the sea's level (not the waves), and with the surface across the glass each
spoilt the half above the line. The game's: below the level it dims the sun for everything it draws and has its shaders
fog by distance; each flight camera carries a part for that (`UnderwaterFog`) with a switch, `fxOn`, which is put off,
and on again when the sea is off. Scatterer's: below its sea's level its haze of the air goes, a green-blue fog by
distance comes over everything and the sun is dimmed (the sky above the line came out green); every frame, after
Scatterer has decided, its switch is thrown back by its own method (`OceanNode.toggleUnderwaterMode`, found by name),
and it throws it again the next frame. None of its settings is touched; with this sea off it goes on as it would.

### The lens in the water (`Sea.cs`: `Lens`, `LensWanted`; `tools/shaderpack/oceanunder.glsl`): made 2026-10-11, 13:40 to 14:55

Asked for: "when half the camera is under water and half of it is above it looks like how a real camera would look in
that situation"; and of the first cut: "looks kinda butt, it doesn't need to be super janky, it can follow the exact
line of the water, it can be nearly straight at times. like have it follow the waters edge".

**What a real camera shows**, and what is made:

- *Two pictures and a line.* Above the water's edge on the glass, the world as ever; below it, the world through water.
  The edge is where the sea AS DRAWN cuts the glass (the camera's near plane, a hand's breadth across and 21 cm ahead):
  the innermost ring of the sea's mesh is drawn once more a frame, from straight above and no wider than the glass, into
  a picture of the water's height (128 points a side, 4 mm apart; the sea's own shader does it when told where the glass
  is), and a pass over the finished picture reads from it how far under the surface each pixel's place on the glass is.
  So the line is the drawn water's own, to the pixel: nearly straight in quiet water, tilting, rising and falling with
  the waves, and nothing else. At the water the mesh is four times finer than elsewhere (3 cm cells, two more rings to
  reach the horizon), or the line is three straight strokes with corners.
- *The edge itself.* Water climbs a wet wall a couple of millimetres. Seen through that little slope the light is
  thrown back down into the water: a thin dark thread along the line (2.2 mm on the glass, seven pixels at 720), with a
  glint of the sky at its upper lip. Nothing is drawn onto it.
- *Water between the glass and things.* Each colour is taken by the metre (red 0.30, green 0.08, blue 0.06 in clear
  water that one sees 30 m through), and what is taken is made up by the water's own light: near things clear, far
  things blue and gone. How far off a thing is comes from the game's picture of distances; the sea's underside and the
  seaweed are not in that, and say it themselves in the picture's fourth number (one added, the sign turned: below
  nought, which nothing else writes and a see-through thing drawn over it does not undo).
- *Light with depth.* The scene is drawn as from the air (see above); the pass cuts each thing's light by how far under
  the surface it lies (red first), so a hull's keel is dimmer and greener than its waterline.
- *The sun gathered by the waves.* The surface is a sheet of lenses: under a crest the light closes in and meets at
  some depth, under a trough it spreads. How fast it closes is how fast the slope changes across the crest, which is
  each patch's "squeeze" (already kept, for the white water) times the patch's wave number; one over one plus a quarter
  of the depth times that is the brightness. On things that face the sun (which way a surface faces is worked out from
  how its distance changes from pixel to pixel) it is the lines of light that run over a hull, from the waves of two
  metres down to a hand; each size counts most at the depth where its light meets and is gone below it. In the water
  itself it shows as shafts: the water's own light along the way is looked at in ten places within ten metres, from the
  waves of two and seven metres only (short waves' light and dark come many times along a way and leave nothing).
- *The underside.* Seen from below the surface is a window of sky straight up and a mirror beyond 48 degrees; the mirror
  shows the water below by the same rule the pass fogs with, so far underside and deep water are one colour where they
  meet at eye level.
- *As behind a domed glass*: things under water are where they are and as large as they are, and a hull meets itself at
  the line. (A flat glass shows them a third larger; that was made first and taken out: the picture under the line swam
  whenever the line moved, and a click on a thing under water would have missed it.)

The pass is on the near camera only while the glass is within reach of the water or under it, after everything
see-through is drawn and before any of the picture's own effects (`CameraEvent.AfterForwardAlpha`). Pixels the glass
is dry at are handed on exactly as they came. Measured: 60 frames a second with it and without (2 m under, the sun's
light on and off by turns).

**Thrown out on the way** (each looked right in one still picture): the edge from the waves' numbers at the camera (a
finger's breadth off the mesh as drawn: a band of the wrong picture along the line); marks for "seen from above" and
"from below" in the picture's fourth number to mend that (they never arrived: see the next); the pass among the
picture's effects, `BeforeImageEffects` (it ran after the colour grading that was already there, on numbers for the
screen, with the fourth number written over); ripples and a band of changing width drawn onto the line (the "janky"
worm the user saw); the distance as "two and more" in the fourth number (an engine's unlit plume adds one to whatever is
behind it and was taken for water at no distance); the sky taken as light and back (its red is below nought before the
grading, and came out green); eight places along the way with the short waves in (a curtain of rain).

**What is wrong with it.** The seabed is dark blue-green under water (the game colours the ground under its sea so),
which makes shallows murky where a real sandy shore is bright; nothing is done about that. No drops or running water on
the glass after a wave has passed over it. The shafts are faint in a quiet sea (they need waves of two metres). The
gathered light is one over a first-order closing, not a count of where the light really lands: lines where there should
be a net of them. A thing's own shadow in the water does not shadow the shafts. Where the picture cannot keep numbers
below nought (the game without TUFX's high-range picture) the underside and the weed are fogged by what is behind them
(seen on Windows with TUFX taken out, 2026-10-11: from under water the weed on the bottom was faint; the underside looked
right). On Windows the three things Direct3D turns about were seen to: the map's picture is laid with
its second axis turned over and its depth the other way (`port.py`, `SPECIAL`), read back and held against the waves'
own heights (`Sea.MapTest`); the pass lays itself on whichever way up the camera's picture lies; and which side of a
face is the front came right by itself.

### The ground as asked and the ground as drawn (found 2026-10-11, 10:00)

The seabed's maps are made by asking the game how high its terrain stands at each point. The answer is the terrain's own
sum, with every bump in it; but the ground the game DRAWS is a mesh that is fine only near the craft being flown, and a
kilometre or two from it the mesh's points are tens of metres apart and the bumps between them are not there. So away
from the craft the maps disagree with what is seen by several metres: the sea thought itself a metre deep where the
drawn bottom was five down. What the eye judged by the maps showed it at once (beds of weed in square blocks, a square of
white "shore" on open water, rings of surf round rocks that are not drawn), and those now go by the ground as drawn
instead, worked back from how far off the picture has the bottom: the dark of the weed beds, and the white edge of the
water. What still goes by the maps: how the waves die out towards the shore, the surf and its foam, the plants' own
places and what floats. Near the craft the two agree. To be made: the maps taken from the terrain as drawn (its meshes
drawn from above into a picture of heights), with the time to the shore worked out from that on the graphics card.

## What is wrong (2026-10-11, 12:40)

1. **From a boat's deck a blowing sea still looks tame.** The white is there in the measured amounts and from the air
   it now looks like the sea (bright fronts, fading streaky patches, rows in a gale); but from five metres up a patch
   lying on the water is a sliver, and what a sailor sees is the breaking crests standing up, which ours do not: the
   front is white laid on a smooth wave, with no roller, and the waves themselves are long and gentle (a fully grown
   sea: see 7). Fresh patches are too clean (a floe of snow with a sharp outline, from 30 m up). The spray is plain
   puffs, and nothing is thrown where a front runs. At 15 m/s the rows hardly show; whether a storm (30 m/s) holds
   together has not been looked at.
2. **From the air the sea does not mirror the clouds** (only a plain sky): the mirrored rays leave last frame's picture.
3. **The surf is a first cut**: waves come in along the shore, curl over and break white, and a sheet runs up the sand;
   but a breaking wave is a smooth lump with foam painted on it, nothing sprays where it falls, crests are short, and the
   sand is not wet where the sheet has been.
4. **The water is one blue.** No colour from depth or from what is in it, no dark wind lanes and smooth slicks, no cat's
   paws under gusts.
5. **What floats leaves only foam on the water** (first cut, 15:35: `Floating.cs` lays a mark into the foam's maps for
   every part at the waterline, by how fast it and the water pass each other; `Spray.Splash` throws spray up at a
   splashdown). Seen: a white patch where a dropped rocket went in, fading in some seconds, and a veil of spray over
   it. Wrong with it: the spray is a solid white lump for its first tenth of a second; no ring spreads from a splash
   and no bubbles stay under the water where a thing went in; a hull under way makes no waves of its own (no bow
   wave, no V behind it); a wake has not been looked at at all (the test craft's engine would not light afloat); when
   a floating craft was snatched up for the test a faint lattice of dots stood in the air where it had lain (not
   found what drew it). Kerbals swimming in waves untried.
6. Under water (made at 14:55, see "The lens in the water" for its own faults): the seabed is dark, no drops on the
   glass, shafts faint in a quiet sea. The sea seen from ABOVE still shows no gathered light on a shallow bottom.
7. A fully grown sea is assumed everywhere (400 km of fetch): a sheltered bay is as rough as the open ocean.
8. Windows: tried on 2026-10-11 (see "On Windows" below). Not there: other worlds' seas, Scatterer beside it, what it
   costs against a game without it.
9. Other mods that draw on the water (Scatterer's own ocean set to show, Parallax's, EVE's) are untried.

### Looked at on 2026-10-11, 16:00 to 16:30: a storm, the foam's sameness, the surf's teeth

The user, shown the sea: "The ocean is really good. I would like to see it in really rough weather, with huge waves that
are capsizing and curling over themselves, huge waves clashing into each other, mist, and all that"; and "the white
water texture looks repetitive when there are big patches of them. It's the same texture over and over."

* **A storm had never been looked at.** At 26 to 28 m/s (waves of ten metres) it was a lumpy sea under sheets of foam,
  with a clear sky, next to no spray, and nothing going over. Made since: the breaking crest goes over (where a front
  runs the mesh is carried forward down the wind by a third of its height over the sea's level, three metres and a
  half at most, and a little down; only within some thirty metres of a low camera, where the mesh can hold it; from a
  near gale on: `_OceanWind`, `Sea.Curl`); spindrift (from 16 m/s more puffs, larger, twice as long in the air, driven
  nearly as fast as the wind); the air over the sea thick from a gale on (one sees 2.2 km along it in a storm, where it
  was 5); a cross swell for the tests (`Sea.Try "cross 70"`). Films: `tools/dev/sea_film.py` (a wait first, for the
  foam: a storm's streaks need two minutes), under the weather held at Storm with the overcast sky (`sky=6:overcast`).
  Sent to the user at 16:28. **Wrong with it:** a fresh patch is a flat white sheet with a clean edge, as large as the
  wave that broke (floes of snow from the mast); the going-over shows only close by; nothing is made where two seas
  meet (no peak thrown up, no spray from it); the spray is veils of upright streaks in some pictures; under the
  overcast there is a white band along the horizon (the cloud sheet's far edge: the weather mod's).
* **The foam's net came round the same** every 5.7 m (its large size) and 1.3 m: in a sheet a hundred metres across the
  eye found it at once. Now each square of the sea reads the net its own way (`laceVaried` in the sea's shader: from a
  place of its own in the picture, a little turned, at a size from seven tenths to half as large again), settled by
  which square it is counted from where the sea's axes begin (`_OceanLaceCount`, to 64 and round again), so it stays
  with the water. The second, turned reading of the small net is gone.
* **The surf's lip was a row of teeth** with white streaks hanging from them, wherever the mesh was coarser than the
  strip of water that goes over. The lip is now thrown only where four or five of the mesh's points span that strip.
  Not yet looked at again from the beach.
* **A far crest was bright blue-green** against the horizon when the camera stood in a trough (my doing, that
  afternoon: the underside had begun to mirror the water's light, and with the lens's pass off "seen from below" still
  went by whether the eye was lower than the place). Which side is seen now goes by the mesh itself, always.

## Next, in order

1. The seabed's maps from the terrain as drawn (see above), so that the surf and the dying-out of waves lie where the
   shore is seen to be. Then spray where a wave falls, wet sand, the surf's lift for what floats.
2. Foam with body: lit and shaded as froth, crescent fronts, streaks down the wind in strong winds; then spray and mist
   (spindrift from crests in a gale, splashes where crests meet), on the explosions mod's smoke where that is installed.
3. The whole sky with its clouds for the sea to mirror from above (with the weather mod).
4. Wakes and splashes; kerbals in the waves.
5. Under water; seaweed; other worlds; fetch from the lie of the land.
6. Windows, with the user's PC (`tools/unityshaders/windows.py`): done on 2026-10-11, see below.

## On Windows (2026-10-11, the Windows PC)

Windows 11, KSP 1.12.5 on Direct3D 11, an NVIDIA GeForce RTX 4080 SUPER at 1280 x 720, a copy of the game with this
family's mods, TUFX and HarmonyKSP and no Scatterer; the development build, and at the end the release builds.

**The shaders.** All four compile as Direct3D shader model 4.0 (`tools/unityshaders/windows.py`: `ocean` 66,868 and
55,928 bytes (64,212 and 50,148 before that evening's spilling crests), `oceanfoam` 22,216, `oceanweed` 4,360 and 3,020, `oceanunder` 9,508). What the porter had to learn
from the compiler: one of a vector's four numbers chosen by a variable can neither be written to inside a kept loop
nor read past the vector's end even where the line guards against it (`Put`, `Pick`). What Direct3D wanted that no
rule could see (`SPECIAL`, `OWN`): the lens's map turned over and its depth run the other way; the lens's pass laid
on whichever way up the camera's picture lies; a face under water turned to the camera however its slope from pixel
to pixel came out; and the white water's place in its own picture taken as it comes (the foam is worked out before
any camera has drawn, with nothing to say which way up anything is).

**Seen**, each beside the same view on OpenGL on the same computer (`-force-glcore`) and alike: the shore east of the
pad in a light wind (green shallows over sand, weed seen through the water, a white line of surf at the edge); a
breeze held at 9 m/s (waves under two metres, a few whitecaps; at the beach a wave standing up, curling over and
running in white); a storm held at 26 m/s (high seas streaked and patched white, spray off the crests); the camera
half under water (a clean line across the glass, the sky above, the underside of the waves below); from under water
(the rippled underside, the bright window to the sun, weed on the bottom); a capsule let down under its parachute
(drops thrown up in arcs, a sheet of white water, the capsule afloat riding the waves).

**Again the same evening** (22:35 to 22:46), after `main`'s newest sea had been merged (crests that spill, with a
roller running down the wind): seven of the looks on Direct3D 11 and the same seven on OpenGL (the calm shore, the
surf in a breeze, open water in a breeze, the camera at the surface, from under water looking up, the storm from 8 m
and from 7 m). Alike, and no exception on either.

**The storm in motion on Direct3D 11** (23:10 to 23:18, asked by the Mac's handoff before the sea is released; wind
held at 26 m/s, `tools/dev/windows/stormfilm.py`): 8 s from 8 m off, 8 s from close by at twelve frames a second,
12 s from 80 m up. The crests that break spill: white water lies on the forward face and is drawn out down it in
ragged tongues from one quarter of a second to the next (`crops.py`: one crest cut out of six frames), with spray
off it. None curled: the 12 s from the air were taken in the picture that shows the curl alone (`test=18`), and not
one of its sixty frames has a mark (`lipcount.py`). With a curl forced on every steep crest (`tune="lip -0.33"`)
25 of 30 frames are marked, and in the sea as it is drawn the lip stands thrown forward over the face. So on
Direct3D the curl is drawn where the shader calls for one, and in an ordinary storm it calls for none in such a
stretch of time. Not set beside OpenGL in motion (the stills of the storm were, and were alike).

**Without TUFX** (moved out of the copy's GameData for it: the game then draws straight to the screen, with 2x smoothed
edges and a picture of numbers for the screen, and every rule about which way up a picture lies takes its other
branch): the thirteen looks again, the line across the glass, a launch and a crash. Alike; under water the weed on the
bottom is faint (what the plan foresaw: the picture cannot keep the weed's distance, and it is fogged as the water
behind it is). Both number checks pass there too.

**Checked by numbers** (two tests left in the development build): `Sea.SeenTest "lay 40 10"` then `"read"`: white
water laid at four places was read back within a metre or two of each, on Direct3D as on OpenGL, so the spray rises
where the water breaks. `Sea.MapTest "keep"` then `"read"` with the clock stopped: the lens's map, read back, was
nearest the waves' own heights the way the lens reads it in six of seven moments (off by 3 to 11 mm in shape as
read; by 7 to 58 mm with an axis turned over), and the sea was in all of it, above the glass's middle too.

**Frames a second** (the game's limit off, overcast with rain and storms near, so the clouds cost what they cost):
54 at the pad with the sea out of sight; 109 over the shore from 70 m in a light wind; in the breeze 62 from 5 m
over open water, 98 from 80 m, 58 half under, 65 in the surf; in the storm 62 from 15 m and 87 from 80 m.

**Found there and mended** (each true on any system): the game's own sea drawn through ours without Scatterer (above,
"The other seas"). On OpenGL with NVIDIA's driver the sea lay flat as glass, with no word in the log: the waves'
textures in the mesh's own program had no place given them (`tools/shaderpack/make_bundle.py` now gives every
texture its place in the vertex program too; `ocean.bundle` packed again; the Mac's OpenGL reads it as before). On
Direct3D the clouds were laid over the nearest waves and, from under water, over the underside, wiping out the mark
the lens's pass goes by: mended in Natural Weather (its Clouds page). `windows.py` packed every shader as writing no
depth, which the sea must. And three lines of error in the log at every start of the sea, on every system ("Material
doesn't have a color property '_OceanMirrorX'", `Y`, `W`): the seaweed reads the mirror's three values from the sea's
material each frame, the first time before the mirror has set them. The sea's material is given them when it is made
(`Surface.cs`); the release build then started without an error line.

**Seen on both Direct3D and OpenGL and left:** near the beach in a light wind, a pale mound of raised water with a
hard edge, some thirty metres across, with a thread of foam running from it. A capsule of a tonne coming in at 5 m/s
whitens some tens of metres of sea round it.

## How it is looked at

`python3 tools/dev/sea_tour.py` takes the ten pictures of the sheet in `dist/progress/ocean-0.1.0-tour.jpg` afresh, each
scene set by itself and photographed while the camera is held there, with what each is written on it (a quarter of an
hour); `sea_tour.py sheet` only lays the pictures together again, after a caption has been changed.


**Every picture of the sea must be taken while the camera has been held at its place for half a minute and more**
(`studio.held`): the foam's maps go with the camera and begin afresh when it comes from far off, and a patch takes a
quarter of a minute to fade. Until 12:40 on 2026-10-11 the scripts looked for a fifth of a second, after a wait at
another moment between which the camera went home to the craft: every picture before then, the sheet sent to the user
among them, under-showed the white water, and the old foam was tuned against that.

The development build, craft on the pad, the camera put over the open sea 5 km east:
`python3 tools/dev/sea_winds.py TAG [load] winds=9,15,22 views=deck,low,air,top hold=40 [maps] [tune=...]` holds each
wind in turn (`NaturalOcean.Sea.Try "wind 15"`, `"go"` to let go) and photographs it from a boat's deck (5 m), 30 m up,
80 m up and straight above, each while held there, with how much of each foam map is white at that moment; `maps` also
writes the four maps and the running fronts out as a picture (`Sea.FoamDump`), and `tune` sets the foam's numbers while
the game runs (`Sea.Tune "eased 0.2 runs 0.5 band 0.05 outright 0.05 plain 4 rows 1 lasts 0.25 spray 1 show 0"`; `show`
1 to 4 puts one size of wave's steep crests into the maps in place of the white, 9 the running fronts).
The lens in the water: `python3 tools/dev/sea_split.py TAG [load] alt=0 distance=4.5 heading=40 [wind=5 swell=0.4]`
holds the camera at the sea's level beside a floating test craft (the quicksave `sea_float`) and takes six pictures;
`sea_splitfilm.py` films it (a film, six frames, and a strip of the line's middle from every second frame, which shows
at once whether the line jumps); `sea_winds.py` has the views `split`, `splitin`, `splitout`, `splitsea`, `below`,
`deep`. `Sea.Tune "climbs 0.0022 mapped 1 rays 1 lens 0"`: how far the water climbs the glass, the edge from the map
(0: from the waves' numbers, as it first was), how much of the gathered light, and one thing shown alone (1: where the
picture says the sea's underside is, how wet the glass, the depth in bands; 2: the picture's fourth number; 3: how much
water lies between). The craft drifts: `load` puts it back.

The camera's height is given above the sea itself (`alt` in `studio.view` and `studio.held`: places are laid out on the
level plane through the craft, and the planet falls away 21 m in 5 km, which for an hour made a 5 m sea look flat).
`NaturalOcean.Sea.Say` reports the sea: wind, heights as shaped and as measured, slopes against Cox and Munk, the
squeezes at which each size of wave breaks and its share of the white, how much of each foam map is white, what the
waves cost. The game's clock runs while testing: two hours after a `load` at midday the sun is setting.
