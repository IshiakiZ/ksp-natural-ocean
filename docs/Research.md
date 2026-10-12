# The sea: what real water does, how it is drawn, and what this game has

Written 2026-10-11, before a line of the mod: the user asked for "a lot of work and a lot of research on how
they're done and how oceans react", for seas that look "as beautiful as possible ... as realistic as possible", that
turn rough with the weather, with "white water bubbles; mist from waves crashing into each other; oceans or waves
that curve over themselves; basically everything that makes it realistic; seaweed and all the stuff". This page is
that research, from what is known of the sea and of how the best real-time seas are made; the plan built on it is in
[Plan](Plan.md). Numbers are for Earth unless said; Kerbin has Earth's gravity at its surface and a tenth of its
radius.

## 1. What makes the waves

**Wind.** Almost every wave on the open sea was raised by wind. A breeze first ruffles the surface into capillary
ripples a centimetre or two long (surface tension pulls them flat again: they die within seconds of the wind
dropping, which is why a gust shows as a dark patch moving over calm water, a *cat's paw*). Longer waves, restored by
gravity, grow as long as the wind blows faster than they travel, taking energy from it; they grow with the wind's
**speed**, the **fetch** (how far the wind has blown over open water) and the **time** it has blown. With enough of
both the sea is *fully developed* and grows no more.

* Fully developed (Pierson and Moskowitz, 1964): significant wave height (the mean of the highest third, which is what
  an observer calls "the height of the waves") **Hs = 0.21 U² / g**, with U the wind at 19.5 m. So 5 m/s gives 0.55 m,
  10 gives 2.1 m, 15 gives 4.8 m, 20 gives 8.6 m, 25 gives 13 m. The dominant period is **T = 0.75 U** seconds (7.5 s at
  10 m/s) and the dominant wavelength g T² / 2π: 88 m at 10 m/s, 350 m at 20.
* Short of that (JONSWAP, 1973, from the North Sea): the spectrum is the same shape with a sharper peak (the energy
  3.3 times more gathered at the dominant wave), at a higher frequency the shorter the fetch:
  peak frequency ω = 22 (g² / U F)^(1/3), energy scale α = 0.076 (U² / F g)^0.22. A 10 m/s wind over 20 km of water
  raises about 0.8 m, not 2.1.
* The largest single wave in a sea is about twice Hs; one in three thousand is 1.9 Hs or more.
* Growing takes hours: a 15 m/s wind needs most of a day and 500 km to raise its full 4.8 m. A sea is therefore
  always behind its wind: rising for hours after the wind gets up, still heaving long after it drops.

**How waves travel** (the dispersion relation). In deep water a wave's speed depends on its length: ω² = g k, so
c = √(g λ / 2π): a 100 m wave runs at 12.5 m/s, a 1 m wave at 1.25. Long waves therefore outrun short ones, and a
storm's waves sort themselves by length as they leave it: far away the long ones arrive first, as **swell**: smooth,
regular, long-crested, 10 to 20 seconds apart, a metre or two high, travelling thousands of kilometres with little
loss. Almost every real sea is a local **wind sea** (short, steep, confused, running with the local wind) lying on
top of one or two swells from elsewhere, from other directions. That is why real water never looks like one noise
pattern. The energy of a group travels at half the speed of its waves (deep water).

**Shape.** Small waves are sinusoids. As they steepen their crests sharpen and their troughs flatten (a trochoid:
the water moves in circles, forward on the crest, back in the trough). A wave cannot be steeper than about 1 in 7
(height to length) or sharper than 120 degrees at the crest: beyond that it **breaks**.

**In which direction.** Energy is spread about the wind's direction: the dominant waves within some 30 degrees of
it, the short ones nearly every way downwind (cos^2s of half the angle, s some 10 to 15 at the peak and falling to
2 or 3 for the short waves; swell is spread far less, s of 50 or more). Short-crestedness is what makes a wind sea
look like a sea and not a washboard.

## 2. What the sea looks like at each wind (the Beaufort scale)

| Force | Wind m/s | Waves | What is seen |
|---|---|---|---|
| 0 | 0 to 0.2 | flat | a mirror |
| 1 | 0.3 to 1.5 | 0.1 m | ripples like scales, no foam |
| 2 | 1.6 to 3.3 | 0.2 m | small wavelets, glassy crests that do not break |
| 3 | 3.4 to 5.4 | 0.6 m | large wavelets, crests begin to break, scattered white horses |
| 4 | 5.5 to 7.9 | 1 m | small waves growing longer, fairly frequent white horses |
| 5 | 8 to 10.7 | 2 m | moderate waves, many white horses, some spray |
| 6 | 10.8 to 13.8 | 3 m | large waves, white foam crests everywhere, spray |
| 7 | 13.9 to 17.1 | 4 m | sea heaps up, foam blown in streaks along the wind |
| 8 | 17.2 to 20.7 | 5.5 m | crests break into spindrift, foam in well-marked streaks |
| 9 | 20.8 to 24.4 | 7 m | high waves, dense streaks, the sea begins to roll, spray dims the view |
| 10 | 24.5 to 28.4 | 9 m | very high waves with overhanging crests, the sea white with foam, heavy rolling |
| 11 | 28.5 to 32.6 | 11.5 m | exceptionally high, the sea covered in foam patches, crests blown to froth |
| 12 | 32.7 and over | 14 m and over | the air filled with foam and spray, the sea completely white |

**White water.** A breaking crest drives air into the water; the bubbles rise and burst over 5 to 20 seconds, so
foam trails behind every breaker and outlives it. The share of the sea that is white: **W = 3.84 × 10⁻⁶ U^3.41**
(Monahan and O'Muircheartaigh, 1980): a thousandth at 5 m/s, a hundredth at 10, four hundredths at 15, a tenth at 20,
over a fifth at 25. Fresh foam is nearly white (it reflects about half the light); as it dies it thins to a lace of
bubbles and the water under it, full of small bubbles, shows pale turquoise for a while. From force 7 the wind lays
the foam out in **streaks** along itself (rows of counter-turning rolls in the water, Langmuir circulation, tens of
metres to a few hundred apart). From about 9 m/s the wind tears drops from the crests (**spindrift**); in a storm
there is a layer of spray over the surface that hides the horizon.

**A whitecap, taken apart** (added 2026-10-11, when the foam was remade to this):

* **What breaks.** Not the dominant waves: in a grown sea they seldom do. Most of the length of breaking crest runs at
  a third to a half of the dominant waves' speed (Phillips, 1985, who wrote the crest length per unit of sea as a
  function of the breaker's speed; measured from the air by Kleiss and Melville, 2010): waves of twenty to fifty metres
  in a 15 m/s wind, three or four metres long in a breeze, a hundred in a storm.
* **How it runs.** A breaker begins at one place on a steep crest, spreads sideways along the crest, and travels with
  it for a part of the wave's period (a tenth of it to the whole of it; about half is usual), then stops. What it
  sweeps is the whitecap's patch: as wide as the stretch of crest that broke, as long as the wave ran meanwhile.
* **Two stages** (Monahan and Woolf, 1989). Stage A is the crest breaking and the dense froth just behind it: bright,
  and little of it, **W_A = 2.92 × 10⁻⁷ U^3.204** (0.17 % of the sea at 15 m/s). Stage B is the patch left behind,
  thinning and opening into lace, with the cloud of small bubbles under it that shows pale green: most of the white
  one sees, and what the larger W above counts. The two shares fix how long each lasts against the other: the bright
  stage about a fifteenth of the whole.
* **The rows.** Wind over water sets the top few metres turning in long rolls laid along the wind, side by side and
  turning opposite ways (Langmuir, 1938, who saw seaweed drawn into lines on the Sargasso Sea). Along the lines where
  two rolls turn downwards together the surface water runs together, at about a hundredth of the wind's speed, and
  what floats is gathered there: foam above all. The rows are metres to hundreds of metres apart, small ones between
  large; they wander, join and part. A gale's streaks are these, of foam that lasts (water that has been breaking for
  hours holds more of what keeps a bubble's skin together).

**Rain** beats small waves flat and peppers the surface with rings; heavy rain makes the sea look smoother and
greyer, each drop throwing a little crown and a rebounding jet.

## 3. Where the sea meets the land

As the water shallows (less than half a wavelength deep) a wave feels the bottom. Its speed falls to √(g d), so it
bunches up: shorter, slower and **higher** (by the fourth root of the depth lost: Green's law), and it turns to face
the shore (refraction: the part of a crest in shallower water lags), which is why waves always arrive nearly
square-on to a beach however they ran outside. It breaks when its height is about **0.78 of the depth**.

How it breaks depends on the beach's slope against the wave's steepness (the surf similarity, ξ = slope / √(H/λ)):

* **Spilling** (ξ under 0.5: gentle beaches, steep wind waves): foam tumbles down the front for a long way.
* **Plunging** (0.5 to 3.3: moderate slopes, long swell): the crest outruns the wave's foot, **curls over** a
  pocket of air and falls in front, throwing up a second splash: the surfer's tube, and the one the user means by
  "waves that curve over themselves".
* **Collapsing and surging** (over 3.3: steep shores): the wave's foot rushes up the slope without a real break.

After breaking the wave is a **bore**, a wall of white water rolling in, losing height; it runs up the beach as a
thin sheet (the swash), leaves a line of foam and wets the sand dark, and drains back under the next. Sets of larger
waves arrive every few minutes (where two swells are in step). Against rock or a cliff a wave throws its water up
as a sheet and a cloud of **mist** that hangs and drifts downwind.

Kerbin's shore by the space centre falls away steeply (17 m above the sea 1.6 km east of the pad, 15 m under it at
1.8 km, 120 m under at 2.2 km): a surging shore, hardly any surf zone. Beaches with long shallows (the islands, the
bays) are where plunging breakers belong.

## 4. What water looks like, and why

* **Reflection.** Looking straight down, water reflects 2% of the light; at 60 degrees from straight down 6%; at 80
  degrees 35%; at grazing angles nearly all (Fresnel). So the far sea is a mirror of the low sky (pale near the
  horizon), the near sea shows what is in the water, and the horizon line is soft. A wave's front and back see
  different parts of the sky, which is most of what makes waves visible at all.
* **Its own colour.** Water takes red light away within a few metres (about 0.45 of it a metre), green slowly (0.06)
  and blue slower still (0.02): clear deep water sends back blue. Living matter and silt near coasts absorb blue:
  coastal water is green, estuary water brown. Over pale sand in a few metres of water the bottom's light comes
  back through the green window: turquoise; over rock or weed, dark.
* **Sun glitter.** The sun is mirrored by every facet of the surface tilted the right way; the rougher the sea the
  wider the glittering path (Cox and Munk, 1954: the mean square slope is 0.003 + 0.00512 U, so the path under a high
  sun is some 10 degrees wide at 2 m/s and 25 at 10). With a low sun it is a long road to the horizon.
* **Light through crests.** A thin crest seen against the light glows green: light entering the back of the wave
  comes out through its front.
* **Foam** scatters light like snow: it takes the colour of the light on it, and shadows show on it.
* **Under clouds** the sea loses the sun's glitter and its own blue (which was scattered sunlight) and reflects
  grey: an overcast sea is grey-green and flat, a storm sea dark slate with white.
* **From under.** Light dims and turns blue-green with depth. Looking up, the whole sky is squeezed into a round
  window 97 degrees wide (Snell's window); outside it the surface is a mirror of the deep. The waves focus sunlight
  into bright moving lines on a shallow bottom (**caustics**) and into shafts in the water. The water is full of
  drifting specks.

## 5. What grows in it

* **Kelp** (large brown seaweeds): on rock, in cool clear water from just under the low-tide line to 20 or 30 m;
  giant kelp reaches the surface and spreads a canopy over it. It sways with every wave.
* **Seagrass**: meadows on sand and mud in sheltered shallows, one to ten metres deep, blades half a metre long.
* **Wrack and rockweed** on rocks between the tide lines; torn weed washed up along the high-water line.
* **Floating weed** gathers in the wind's streaks.

## 6. How the best real-time seas are made

1. **The waves as a sum of very many sine waves, by Fourier transform** (Tessendorf, 2001, used in film since
   *Titanic* and in games since). A patch of sea is described by how much energy it has at each wavelength and
   direction (the *spectrum*: sections 1 and 2 as a formula). Each of some 16,000 to 260,000 waves gets a random
   start, turns at the speed its length dictates, and an inverse fast Fourier transform adds them all up into a
   height at every point of a square patch that repeats. **Choppiness**: the same sum also gives how far each bit of
   water moves sideways (towards the crests), which sharpens crests and flattens troughs as real water does. Where
   that sideways squeeze folds the surface over itself (its Jacobian goes below nothing) the wave is breaking: that
   is where to put foam and throw spray.
2. **Better spectra** than Tessendorf's first (Horvath, 2015, *Empirical directional wave spectra for computer
   graphics*): JONSWAP with its depth correction (TMA) and a measured directional spread, with wind speed, fetch,
   depth and a separate swell as the dials. This is what makes a force 3 and a force 9 look like themselves.
3. **Several patches of different sizes at once** (cascades: say 1 km, 150 m, 20 m and 3 m), each with its own
   band of wavelengths, added together: detail from kilometres to centimetres with no visible repeat.
4. **A mesh that is fine near the eye and coarse far off**, displaced by those patches: a grid projected from the
   screen (Johanson, 2004; Scatterer and Proland), or nested square rings that slide with the camera in whole
   steps (clipmaps: Crest). Waves too small for the mesh at a distance are not thrown away but turned into the
   surface's *roughness* there (Bruneton, 2010): the far sea then glitters as widely as it should.
5. **Light.** Fresnel reflection of the sky (and clouds, and the sun as a glittering road), the water's own colour
   by depth with the bottom showing through and bent by the surface, the green of light through crests, foam from
   the fold measure with a memory so that it trails and fades, shadows.
6. **The shore**: the depth under each point (from the terrain) fades the deep-water waves out and brings in waves
   that run at the shore, bunch up, steepen and break where they are 0.78 of the depth, with a breaking shape whose
   crest is pushed ahead of its foot, foam after, and spray at the lip. (Uncharted 4 and Horizon Forbidden West
   place such waves by hand along a coast or from a map of distance to the shore.)
7. **Things in the water**: the same wave heights read back for whatever floats (it rides the waves it is seen
   on), wakes and splashes added as their own small waves (wave particles, or a small simulated square round the
   craft), spray and mist as particles.
8. **Under water**: fog by depth, the surface seen from below with Snell's window, caustics, shafts.

## 7. What this game has

* **The game's own sea** is a second planet-sized mesh (the ocean PQS) with a flat blue shader and scrolling bump
  maps; sea level is height 0; a part floats by `PartBuoyancy`, which reads its depth below a level it is told
  (`waterLevel`, plus the height of the sea); a splash effect when something hits the water; under water, a tint.
* **Scatterer** (installed here, by another author, GPL) draws its own: a screen-projected grid moved by four
  Fourier patches worked out on the graphics card, whitecaps from the fold measure, sky reflection, the bottom seen
  through shallow water, caustics, shafts under water. Seen here on 2026-10-11 (`~/.ksp-ai-bridge/pictures/sea/s0_*`):
  a dark navy sea with a low swell and fine ripples, a glitter path, flecks of white from high up, turquoise under
  water; it draws at 80 to 90 frames a second with nothing else in view. It is one sea state always: it knows no
  weather. Its floating-on-waves needs a kind of shader (compute) that this Mac's OpenGL has not got. Its
  settings must never be changed from outside (it writes them back to the user's file).
* **What it has not got, and the user asked for:** a sea that follows the weather; foam that trails and gathers in
  streaks; spray and mist; breakers that curl on a beach, bores and swash; weed; rain on the water.
* **Other seas**: Laythe (0.8 of Kerbin's gravity: for the same wind, waves a quarter higher and slower), Eve's (1.7
  of it: lower, quicker; not water).

## 8. When a wave breaks, and how (looked up 2026-10-11, after the user's verdict on the curls)

The mod had been given a breaking crest that rolled into a round curl with a tube under it, on every breaking crest of
a storm. The user: "It's very geometric and not natural and organic. The curls should happen only like how they do in
real life: when they happen, how they happen, why they happen." So, what does happen.

**Why a wave breaks at all.** The water at a wave's crest moves forward with the wave, more slowly than the wave
itself. When a crest steepens, the water at its top speeds up; once it is moving at about 0.85 of the crest's own speed
the crest can no longer hold it and it goes over the front (crest water speed over crest speed, 0.85:
[Barthelemy and others, 2018](https://data-ww3.ifremer.fr/BIB/Barthelemy_etal_JFM2018.pdf)). On deep water what
steepens a crest is waves running together: a wave passing through the middle of its group, where it is highest;
fast waves overtaking slow ones; a sea running against a current; the wind pushing on the crest. A crest about to
break slows a little (to about 0.85 of what its length would give) and leans, and the white water that follows runs
at about 0.8 of the wave's speed ([Rapp and Melville, 1990](https://airsea.ucsd.edu/wp-content/uploads/sites/10/2019/06/1990_Rapp_Melville-A_Mathematical_and_Physical_Sciences_vol_331.pdf);
[crest speeds of unsteady waves](https://arxiv.org/pdf/1910.07946)).

**Two ways of breaking, and which the open sea does.**

* **Spilling.** The crest crumbles: white water appears at the top and slides down the forward face as a tumbling,
  bubbling layer (a roller), growing down the face while the wave runs on and leaving a patch of foam behind. No
  hollow, no tube. It happens when the crest is only just too steep, **and it is nearly all the breaking there is on
  deep water**: a whitecap is a spilling breaker. In the laboratory, wave groups brought together gently spill
  and take about a tenth of the group's energy.
* **Plunging.** The front face stands up to the vertical and the top of the crest is thrown forward as a jet, which
  falls in an arc and strikes the water ahead, throwing up a splash as high as the wave. Under the jet for a
  moment there is a hollow. It takes waves brought together hard (in the laboratory, a quarter of the group's
  energy goes), so **on deep water it is uncommon and short**: a steep crest in a confused sea, where two seas cross,
  against a current, or the largest waves of a storm. The jet's water moves faster than the wave: 1.3 to 1.7 times
  its speed where it comes down, more at its very tip.
* Between the two there is no line: a strong spill begins with a small jet at the crest that falls at once and
  becomes the roller ([Breaking wave](https://en.wikipedia.org/wiki/Breaking_wave); a
  [2024 computation](https://epjst.epj.org/articles/epjst/abs/first/11734_2024_Article_1207/11734_2024_Article_1207.html):
  spilling at moderate steepness, "a dramatic plunging" at larger).

**At a shore it is the bottom that decides.** A wave coming into shallow water slows, shortens and stands up. Which
way it breaks goes by the beach's slope against the wave's steepness (the Iribarren number: the slope over the root
of height over length out at sea): below about 0.5 it spills, a long way out, over a wide surf zone (steep seas on
flat sand); from 0.5 to about 3 it plunges (**the curl of the surfer's wave: long low swell on a shelving bottom**);
above that it does not break so much as surge up the beach ([Iribarren number](https://en.wikipedia.org/wiki/Iribarren_number)).
The figures are rough and the kinds shade into one another.

**What a plunging crest's shape really is.** Not a circle. The space under the overturning crest is close to an
ellipse about 1.7 times as long as it is wide (the root of three), lying tilted, its long way running down and
forward from under the crest ([New, 1983](https://www.cambridge.org/core/journals/journal-of-fluid-mechanics/article/abs/class-of-elliptical-freesurface-flows/A9F414B83321C675C69F9A86F72124DF));
the lip above it is a tongue that thins to an edge and falls as thrown water falls; and the rest of the wave is
still a wave: a long back and a face no deeper than the wave is high. Along the crest nothing is even: the lip is
further out here than there, comes down first in one place and the breaking runs along the crest from it, its edge
torn into fingers and drops.

**How long, how much.** A breaking lasts some half of the wave's period and takes a stretch of the crest, not
all of it: metres on a small wave, tens of metres on a large one. Of the sea's surface a few thousandths to a few
hundredths is breaking at any moment even in a gale (the figures the mod already goes by: section 2).

**What follows for the mod.**

1. On deep water a breaking crest **spills**: white water going down the forward face from the crest, moving, with
   spray off it. That is the usual breaker, and it has no curl.
2. A **plunge** only where the crest is squeezed well past the level at which it breaks (the few most violent
   breakings), and where two wave trains cross; its curl small against the wave and seldom fully over.
3. The **curl belongs at a shelving shore**: there by the Iribarren number, from the swell's height and length and
   the seabed's slope, all of which the mod knows.
4. Its shape from the tilted ellipse and a falling tongue, not a circle; uneven along the crest; nothing dug out of
   the sea in front of it deeper than the wave is high.
5. The foam's pattern is not to be laid on a surface that is being drawn out: plain water and plain froth there.

