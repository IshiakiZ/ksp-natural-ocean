using System;
using System.Collections.Generic;
using UnityEngine;

namespace NaturalOcean
{
    /// <summary>
    /// What floats rides the waves that are drawn. The game floats every part by its own reckoning (PartBuoyancy), against
    /// a water level that each part carries: nought, a flat sea. Before the game's physics runs, every part near the sea
    /// is told how high the water stands where it is just now, and the game's own buoyancy does the rest: a boat heaves
    /// and pitches, a capsule bobs, a kerbal swims up and down the swell.
    ///
    /// One thing the game does must be kept from happening over and over. A part that enters the water is, to the game, a
    /// splashdown: for three seconds it is dragged thirty times as hard, and it may break. On a flat sea that happens once.
    /// In waves a floating thing's parts leave the water and come back with every crest, and each time would be a splashdown
    /// (Scatterer's makers found seaplanes unusable for it). So a part that was in the water within the last few seconds is
    /// taken as never having left it: only what comes down out of the air is a splashdown, as in the game.
    /// Nothing of the game's physics settings is changed.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class Floating : MonoBehaviour
    {
        /// <summary>How long after a part was last in the water its coming back is not a splashdown, s.</summary>
        static double Lately = 5.0;
        readonly Dictionary<uint, double> wet = new Dictionary<uint, double>();
        // (how hard the game drags each part through water: its drag force over the square of its speed)
        readonly Dictionary<uint, float> held = new Dictionary<uint, float>();
        /// <summary>Whether what floats is carried by the water's own motion (see Step).</summary>
        public static bool Carried = true;
        /// <summary>Whether what floats leaves foam on the water (see Step, and Foam.Mark).</summary>
        public static bool Marks = true;
        public static int CarriedParts;
        readonly List<uint> gone = new List<uint>();
        bool leftFlat = true;
        int steps;
#if DEV
        /// <summary>(Development build: every part told this one water level instead of the waves', to see what the game makes of it; not a number: the waves'.)</summary>
        public static float HeldLevel = float.NaN;
        public static string Carry(string v) { Carried = v != "off"; return "carried by the water: " + Carried; }
        /// <summary>(Development build: how long after a part was last in the water its coming back is no splashdown, s; 5 as shipped. For trying a splash by lifting a floating craft and letting it drop.)</summary>
        public static string Late(string v) { Lately = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture); return "lately: " + Lately + " s"; }
        public static string Hold(string v) { HeldLevel = v == "go" ? float.NaN : float.Parse(v, System.Globalization.CultureInfo.InvariantCulture); return "held level " + HeldLevel; }
        /// <summary>(Development build: the craft being flown as the game's buoyancy and drag have it: the whole, then its heaviest parts.)</summary>
        public static string Parts()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return "no craft";
            var b = new System.Text.StringBuilder();
            Vector3d up = (v.CoMD - v.mainBody.position).normalized;
            double lift = 0.0, weight = v.totalMass * v.graviticAcceleration.magnitude, dragUp = 0.0;
            var list = new List<Part>(v.parts);
            list.Sort((x, y) => (y.mass + y.GetResourceMass()).CompareTo(x.mass + x.GetResourceMass()));
            foreach (Part p in v.parts)
            {
                if (p.partBuoyancy != null) lift += Vector3d.Dot((Vector3d)p.partBuoyancy.effectiveForce, up);
                dragUp += Vector3d.Dot(-(Vector3d)p.dragVectorDir * p.dragScalar, up);
            }
            b.Append(v.parts.Count).Append(" parts, ").Append(v.totalMass.ToString("F2")).Append(" t, weight ").Append(weight.ToString("F1")).Append(" kN; buoyancy up ").Append(lift.ToString("F1")).Append(" kN; the game's drag up ").Append(dragUp.ToString("F1"))
             .Append(" kN; rising ").Append(v.verticalSpeed.ToString("F2")).Append(" m/s; carried parts ").Append(CarriedParts).Append("\n");
            int shown = 0;
            foreach (Part p in list)
            {
                PartBuoyancy pb = p.partBuoyancy;
                if (pb == null) continue;
                Rigidbody r = p.rb;
                b.Append(p.partInfo != null ? p.partInfo.name : p.name).Append(" ").Append((p.mass + p.GetResourceMass()).ToString("F2")).Append(" t: level ").Append(pb.waterLevel.ToString("F2")).Append(", depth ").Append(pb.depth.ToString("F2"))
                 .Append(", under ").Append(pb.submergedPortion.ToString("F2")).Append(", buoyant ").Append(Vector3d.Dot((Vector3d)pb.effectiveForce, up).ToString("F1")).Append(" kN (").Append(pb.buoyantGeeForce.ToString("F2")).Append(" g), slow ").Append(pb.slow)
                 .Append(", body drag ").Append(r != null ? r.drag.ToString("F2") : "-").Append(", the game's drag ").Append(p.dragScalar.ToString("F1")).Append(" kN at ").Append(p.dragVectorMag.ToString("F2")).Append(" m/s (up ").Append(Vector3.Dot(p.dragVector, (Vector3)up).ToString("F2"))
                 .Append("), in water x").Append(p.submergedDragScalar.ToString("F2")).Append(", pressure ").Append(p.submergedDynamicPressurekPa.ToString("F2")).Append(" kPa\n");
                if (++shown >= 7) break;
            }
            return b.ToString();
        }
#endif
        public static int Lifted;               // how many parts were told a water level at the last step (for the report)
        public static float Highest, Lowest;    // the highest and lowest of those levels

        void Start() { TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Earlyish, Step); }

        void OnDestroy()
        {
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.Earlyish, Step);
            Flat();
        }

        /// <summary>Every part's water level back to the flat sea's (when the sea is switched off, or not drawn in waves here).</summary>
        void Flat()
        {
            if (leftFlat) return;
            leftFlat = true;
            if (FlightGlobals.VesselsLoaded == null) return;
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null || v.parts == null) continue;
                foreach (Part p in v.parts) if (p != null && p.partBuoyancy != null) p.partBuoyancy.waterLevel = 0.0;
            }
        }

        void Step()
        {
            if (Ocean.Float == null || !Ocean.Float || Ocean.On == null || !Ocean.On || !Sea.Drawn || !FlightGlobals.ready) { Flat(); Lifted = 0; return; }
            CelestialBody body = FlightGlobals.currentMainBody;
            if (body == null || !body.ocean) { Flat(); Lifted = 0; return; }
            double now = Planetarium.GetUniversalTime();
            int lifted = 0, carried = 0; float highest = -1e9f, lowest = 1e9f;
            leftFlat = false;
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null || v.packed || v.mainBody != body || v.parts == null) continue;
                // (nothing more than a hundred metres above the sea needs telling; what is high up has the flat level, which it cannot reach)
                if (v.altitude > 100.0) continue;
                foreach (Part p in v.parts)
                {
                    if (p == null) continue;
                    PartBuoyancy b = p.partBuoyancy;
                    if (b == null) continue;
                    float level = Sea.WaterAt((Vector3d)p.transform.TransformPoint(p.CenterOfBuoyancy), out Vector3 flow, out Vector2d place);
#if DEV
                    if (!float.IsNaN(HeldLevel)) level = HeldLevel;
#endif
                    b.waterLevel = level;
                    lifted++; if (level > highest) highest = level; if (level < lowest) lowest = level;
                    uint id = p.flightID;
                    // ---- carried by the water. The game holds a part in the water by damping its speed: a slow one (under 1.6 m/s)
                    // loses its speed at a rate of 2 a second, as if the water stood still. But the water of a wave is itself
                    // moving, a metre a second and more in a fresh wind, up as the crest comes and down behind it, and a
                    // floating thing should be held to THAT. Left as the game has it, the damping holds a craft where it is
                    // while the wave goes up past it: measured, a rocket with little buoyancy to spare followed a tenth of the
                    // water's rise and fall and was at times three metres under. So the part is given the push that makes the
                    // game's damping pull it towards the water's speed instead of towards rest: the damping's rate times the
                    // water's speed. (Read from the game's own steps: Sea.Il in the development build.)
                    if (Carried && p.submergedPortion > 0.02)
                    {
                        Rigidbody own = p.rb;
                        if (own != null)
                        {
                            if (own.drag > 0f) own.AddForce(flow * own.drag, ForceMode.Acceleration);
                            // (A part moving faster than 1.6 m/s the game hardly damps at all, so a craft dropping into a trough
                            // fell free, went three metres under and was slow to come back. Up and down only, and only for what
                            // is not travelling fast over the water (a boat under way, a seaplane on its floats, are left to
                            // the game), the part is held to the water's rise and fall at the slow rate still.)
                            Vector3 upHere = (Vector3)FlightGlobals.getUpAxis(body, (Vector3d)p.transform.position);
                            Vector3 moves = own.velocity + (Vector3)Krakensbane.GetFrameVelocity();
                            float rising = Vector3.Dot(moves, upHere), across = (moves - upHere * rising).magnitude;
                            float more = Mathf.Max(0f, 2f - own.drag) * Mathf.Clamp01((float)p.submergedPortion * 2f) * Mathf.Clamp01((10f - across) / 5f);
                            if (more > 0f) own.AddForce(upHere * (more * (Vector3.Dot(flow, upHere) - rising)), ForceMode.Acceleration);
                            carried++;
                        }
                        // (A fast part the game drags by the square of its speed as well, as it drags it through air but with
                        // water's weight: that too is taken against the moving water. How hard the game drags the part, its
                        // force over its speed squared, is read from the part as the game last left it.)
                        Vector3 moving = p.dragVector;
                        float speed2 = p.dragVectorSqrMag;
                        if (!held.TryGetValue(id, out float hold)) hold = 0f;
                        if (speed2 > 0.04f && p.dragScalar > 0f) { float nowHold = p.dragScalar / speed2; hold = hold <= 0f ? nowHold : Mathf.Lerp(hold, nowHold, 0.15f); held[id] = hold; }
                        else if (p.dragScalar <= 0f && hold > 0f) { hold = 0f; held[id] = 0f; }
                        Rigidbody body2 = p.Rigidbody;
                        if (hold > 0f && body2 != null)
                        {
                            Vector3 through = moving - flow;
                            Vector3 force = hold * (moving.magnitude * moving - through.magnitude * through);
                            // (never more than would bring the part to the water's own speed within this step: the sum is worked out a step late)
                            float most = body2.mass * flow.magnitude / Mathf.Max(Time.fixedDeltaTime, 1e-3f);
                            if (force.sqrMagnitude > most * most) force = force.normalized * most;
                            body2.AddForceAtPosition(force, p.transform.position, ForceMode.Force);
                        }
                    }
                    // ---- the mark it leaves on the water. A hull that moves through the water, or that the water moves past,
                    // churns it white where the two meet: a patch as wide as the part, the whiter the faster (nothing below a
                    // quarter of a metre a second, all of it from two and a half), laid into the foam's own maps, where it lies
                    // and fades as all foam does; so a boat under way trails a wake, and a capsule riding a sea has froth round
                    // it. A part that comes down into the water out of the air throws up a ring wider than itself.
                    if (Marks && p.submergedPortion > 0.02)
                    {
                        Rigidbody mine = p.rb ?? p.Rigidbody;
                        Vector3 through = mine != null ? mine.velocity + (Vector3)Krakensbane.GetFrameVelocity() - flow : Vector3.zero;
                        float atLine = Mathf.Clamp01((float)p.submergedPortion * 8f) * Mathf.Clamp01((1f - (float)p.submergedPortion) * 8f);
                        float wide = Mathf.Sqrt(Mathf.Max((float)p.radiativeArea, 0.2f) / 12.566f);
                        float churned = atLine * Mathf.SmoothStep(0f, 1f, (through.magnitude - 0.25f) / 2.25f) * 0.9f;
                        bool cameDown = b.splashed && !(wet.TryGetValue(id, out double last) && now - last < Lately);
                        if (cameDown)
                        {
                            churned = 1f;
                            Vector3d here = (Vector3d)p.transform.TransformPoint(p.CenterOfBuoyancy);
                            Vector3 upHere2 = (Vector3)FlightGlobals.getUpAxis(body, here);
                            // (the spray from where the water's surface is, under or over the part's middle)
                            if (through.magnitude > 1.5f) Spray.Splash((Vector3)here + upHere2 * (level - (float)body.GetAltitude(here)), upHere2, through.magnitude, wide);
                            wide *= Mathf.Clamp(1.5f + through.magnitude / 4f, 1.5f, 4f);
                        }
                        if (churned > 0.02f) Foam.Mark(place, wide * 1.15f, churned);
                    }
                    if (b.splashed) wet[id] = now;
                    else if (wet.TryGetValue(id, out double when) && now - when < Lately)
                    {
                        // (in the water a moment ago: if the next crest takes it again, that is no splashdown)
                        b.splashed = true; b.wasSplashed = true;
                    }
                }
            }
            Lifted = lifted; Highest = lifted > 0 ? highest : 0f; Lowest = lifted > 0 ? lowest : 0f;
            CarriedParts = carried;
            // (parts long dry, or gone, forgotten now and then)
            if (++steps % 500 == 0)
            {
                gone.Clear();
                foreach (var pair in wet) if (now - pair.Value > 60.0) gone.Add(pair.Key);
                foreach (uint id in gone) { wet.Remove(id); held.Remove(id); }
            }
        }
    }
}
