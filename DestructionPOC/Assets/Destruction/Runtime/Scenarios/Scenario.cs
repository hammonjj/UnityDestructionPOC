using System;
using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>A reproducible demonstration: geometry, instructions, expected result, framing and trigger.</summary>
    public sealed class Scenario
    {
        public string id;
        public string title;
        public string instruction;
        public string expected;
        public Func<List<PieceDef>> build;
        public Action<DestructionSettings> configure;
        public Action<DestructionWorld> postBuild;
        public string triggerLabel;
        public Action<DestructionWorld> trigger;
        public string[] highlight = Array.Empty<string>();
        public Vector3 cameraPivot;
        public float cameraDistance = 22f;
        public float cameraYaw = -35f;
        public float cameraPitch = 22f;
    }

    /// <summary>Procedural, authored geometry. No external art or mesh preparation.</summary>
    public static class ScenarioLibrary
    {
        const int C = DestructionSettings.Concrete;

        public static List<Scenario> All()
        {
            return new List<Scenario>
            {
                IntactBuilding(),
                LocalDamage(),
                LossOfSupport(),
                ThinConnection(),
                HangingFloor(),
                ArrestedFloor(),
                SecondaryCollapse(),
                PersistentRubble(),
            };
        }

        public static Scenario ById(string id) => All().Find(s => s.id == id);

        // ------------------------------------------------------------------ building blocks

        /// <summary>
        /// Column-and-slab frame: 3×3 columns on a 4 m grid, each floor a 4×4 grid of 2 m slab panels, with an
        /// upper-floor back wall. Every panel bears on one column corner and its neighbours.
        /// </summary>
        public static List<PieceDef> Frame(int storeys, bool walls, float storeyHeight = 3f)
        {
            var list = new List<PieceDef>();
            const float slab = 0.25f;
            const float col = 0.4f;
            float baseY = 0f;
            for (int s = 0; s < storeys; s++)
            {
                for (int ix = -1; ix <= 1; ix++)
                for (int iz = -1; iz <= 1; iz++)
                {
                    list.Add(PieceDef.Box($"Column F{s} {Name(ix)}{Name(iz)}",
                        new Vector3(ix * 4f, baseY + storeyHeight * 0.5f, iz * 4f),
                        new Vector3(col, storeyHeight, col), PieceKind.Column));
                }

                if (walls && s >= 1)
                {
                    for (int ix = 0; ix < 2; ix++)
                    {
                        float x0 = ix == 0 ? -3.8f : 0.2f;
                        list.Add(PieceDef.Box($"Wall F{s} back {ix}",
                            new Vector3(x0 + 1.8f, baseY + storeyHeight * 0.5f, 3.875f),
                            new Vector3(3.6f, storeyHeight, 0.25f), PieceKind.Wall));
                    }
                }

                float sy = baseY + storeyHeight + slab * 0.5f;
                for (int px = 0; px < 4; px++)
                for (int pz = 0; pz < 4; pz++)
                {
                    list.Add(PieceDef.Box($"Slab F{s + 1} {px}{pz}",
                        new Vector3(-3f + px * 2f, sy, -3f + pz * 2f),
                        new Vector3(2f, slab, 2f), PieceKind.Slab));
                }
                baseY += storeyHeight + slab;
            }
            return list;
        }

        static string Name(int i) => i < 0 ? "L" : i > 0 ? "R" : "M";

        static int[] ConnectionsOf(DestructionWorld w, string pieceName)
        {
            var g = w.Graph;
            int idx = g.pieces.FindIndex(p => p.name == pieceName);
            if (idx < 0) return Array.Empty<int>();
            return g.adjacency[idx].ToArray();
        }

        static int[] ConnectionsBetween(DestructionWorld w, string a, string b)
        {
            var g = w.Graph;
            int ia = g.pieces.FindIndex(p => p.name == a);
            int ib = b == "ground" ? Connection.Ground : g.pieces.FindIndex(p => p.name == b);
            if (ia < 0) return Array.Empty<int>();
            var c = ib == Connection.Ground ? g.GroundConnection(ia) : g.Find(ia, ib);
            return c == null ? Array.Empty<int>() : new[] { c.id };
        }

        // ------------------------------------------------------------------ 1–2: building

        static Scenario IntactBuilding() => new Scenario
        {
            id = "intact",
            title = "1 · Intact stability",
            instruction = "Press nothing. Watch for 60 s (the HUD timer counts up).",
            expected = "Nothing moves, the failure log stays empty, every load ratio stays below 1/safety factor.",
            build = () => Frame(2, true),
            cameraPivot = new Vector3(0f, 3f, 0f), cameraDistance = 24f,
        };

        static Scenario LocalDamage() => new Scenario
        {
            id = "local",
            title = "2 · Local damage",
            instruction = "Damage tool: click the highlighted first-floor panel three times (or press T). Two clicks and a wait also work: the weakened joints then fail by overload.",
            expected = "Only that panel breaks out and drops to the ground; the frame around it stays up.",
            build = () => Frame(2, true),
            highlight = new[] { "Slab F1 11" },
            triggerLabel = "Damage panel ×3",
            trigger = w =>
            {
                int i = w.Graph.pieces.FindIndex(p => p.name == "Slab F1 11");
                for (int k = 0; k < 3; k++) w.Damage(i, w.Settings.tools.clickDamage);
            },
            cameraPivot = new Vector3(-1f, 2.5f, -1f), cameraDistance = 18f, cameraYaw = -25f, cameraPitch = 18f,
        };

        // ------------------------------------------------------------------ 3: bridge

        /// <summary>Four-segment deck between two end walls with a middle pier.</summary>
        static List<PieceDef> Bridge()
        {
            var list = new List<PieceDef>
            {
                PieceDef.Box("Abutment W", new Vector3(-5.3f, 1.5f, 0f), new Vector3(0.6f, 3f, 3f), PieceKind.Wall),
                PieceDef.Box("Abutment E", new Vector3(5.3f, 1.5f, 0f), new Vector3(0.6f, 3f, 3f), PieceKind.Wall),
                PieceDef.Box("Pier", new Vector3(0f, 1.5f, 0f), new Vector3(0.6f, 3f, 2.4f), PieceKind.Column),
            };
            // Deck 2 and 3 meet over the pier with an expansion gap, so each span bears on its own support.
            list.Add(PieceDef.Box("Deck 1", new Vector3(-4.2f, 3.15f, 0f), new Vector3(2.8f, 0.3f, 3f), PieceKind.Slab));
            list.Add(PieceDef.Box("Deck 2", new Vector3(-1.4125f, 3.15f, 0f), new Vector3(2.775f, 0.3f, 3f), PieceKind.Slab));
            list.Add(PieceDef.Box("Deck 3", new Vector3(1.4125f, 3.15f, 0f), new Vector3(2.775f, 0.3f, 3f), PieceKind.Slab));
            list.Add(PieceDef.Box("Deck 4", new Vector3(4.2f, 3.15f, 0f), new Vector3(2.8f, 0.3f, 3f), PieceKind.Slab));
            return list;
        }

        /// <summary>Severs every connection of a piece and pushes it out of the way, so it no longer
        /// supports anything by contact. The push carries no damage.</summary>
        static void KnockOut(DestructionWorld w, string pieceName, Vector3 pushFrom, float impulse)
        {
            w.Sever(ConnectionsOf(w, pieceName));
            int i = w.Graph.pieces.FindIndex(p => p.name == pieceName);
            if (i < 0) return;
            Vector3 c = w.Graph.pieces[i].center;
            w.Explode(c + pushFrom, 1.2f, 0f, impulse);
        }

        static Scenario LossOfSupport() => new Scenario
        {
            id = "support",
            title = "3 · Loss of support",
            instruction = "Press T (or explode the middle pier) to remove the load-bearing pier.",
            expected = "The middle deck joints are overloaded and turn into residual hinges; the deck sags into a V and may tear free. The abutments stand.",
            build = Bridge,
            highlight = new[] { "Pier" },
            triggerLabel = "Remove pier",
            trigger = w => KnockOut(w, "Pier", new Vector3(0f, -0.6f, -0.9f), 60000f),
            cameraPivot = new Vector3(0f, 2f, 0f), cameraDistance = 18f, cameraYaw = -20f, cameraPitch = 12f,
        };

        // ------------------------------------------------------------------ 3b: capacity vs connectivity

        static List<PieceDef> PierWall()
        {
            var list = new List<PieceDef>();
            for (int i = 0; i < 5; i++)
            {
                float x = -2f + 0.4f + i * 0.8f;
                float width = i == 4 ? 0.4f : 0.76f; // pier 5 is the narrow one
                list.Add(PieceDef.Box($"Pier {i + 1}", new Vector3(x, 2.5f, 0f), new Vector3(width, 5f, 0.4f), PieceKind.Wall));
            }
            list.Add(PieceDef.Box("Balcony", new Vector3(0f, 4.125f, 1.7f), new Vector3(4f, 0.25f, 3f), PieceKind.Slab));
            return list;
        }

        static Scenario ThinConnection() => new Scenario
        {
            id = "thin",
            title = "3b · Capacity vs connectivity",
            instruction = "Press T to cut the balcony from piers 1–4. Only narrow pier 5 still connects it.",
            expected = "A graph path to the ground still exists, but the narrow connection is overloaded (q ≥ 2) and fails; its residual is far too weak and the balcony drops.",
            build = PierWall,
            highlight = new[] { "Pier 5" },
            triggerLabel = "Cut piers 1–4",
            trigger = w =>
            {
                var ids = new List<int>();
                for (int i = 1; i <= 4; i++) ids.AddRange(ConnectionsBetween(w, "Balcony", $"Pier {i}"));
                w.Sever(ids);
            },
            cameraPivot = new Vector3(0f, 3f, 1f), cameraDistance = 15f, cameraYaw = -50f, cameraPitch = 18f,
        };

        // ------------------------------------------------------------------ 4: partial attachment

        /// <summary>A 4 m × 4 m floor held by a wall on one edge and two props on the other. The hanging
        /// variant sits 5 m up so the floor can swing almost vertical without reaching the ground.</summary>
        static List<PieceDef> HangingFloorGeometry(bool arrestBlock)
        {
            float h = arrestBlock ? 3f : 5f;
            var list = new List<PieceDef>
            {
                PieceDef.Box("Back wall", new Vector3(0f, (h + 2.5f) * 0.5f, -0.2f), new Vector3(5f, h + 2.5f, 0.4f), PieceKind.Wall),
                PieceDef.Box("Floor", new Vector3(0f, h + 0.125f, 2f), new Vector3(4f, 0.25f, 4f), PieceKind.Slab),
                PieceDef.Box("Prop L", new Vector3(-1.8f, h * 0.5f, 3.8f), new Vector3(0.3f, h, 0.3f), PieceKind.Column),
                PieceDef.Box("Prop R", new Vector3(1.8f, h * 0.5f, 3.8f), new Vector3(0.3f, h, 0.3f), PieceKind.Column),
            };
            if (arrestBlock)
                list.Add(PieceDef.Box("Low wall", new Vector3(0f, 0.8f, 3.2f), new Vector3(4.6f, 1.6f, 0.4f), PieceKind.Wall));
            return list;
        }

        static void KnockProps(DestructionWorld w)
        {
            KnockOut(w, "Prop L", new Vector3(0f, -0.3f, -0.6f), 6000f);
            KnockOut(w, "Prop R", new Vector3(0f, -0.3f, -0.6f), 6000f);
        }

        static Scenario HangingFloor() => new Scenario
        {
            id = "hanging",
            title = "4a · Partial attachment → separation",
            instruction = "Press T to knock out both props. Watch the floor hinge, sag and hang from the wall edge. Press F1 and Inspect the floor: its residual q sits just above 1. Damage the floor to tear it sooner; raise 'Residual strength' to make it hold.",
            expected = "The wall interface is overloaded (q ≫ 1) and becomes a residual hinge. The floor sags plastically and hangs for a few seconds; its own hanging weight keeps residual q above 1, so residual damage grows until the hinge tears. Compare 4b: same floor and hinge, but contact support stops the damage. No timer.",
            build = () => HangingFloorGeometry(false),
            highlight = new[] { "Prop L", "Prop R" },
            triggerLabel = "Knock out props",
            trigger = KnockProps,
            cameraPivot = new Vector3(0f, 3.5f, 2f), cameraDistance = 15f, cameraYaw = -60f, cameraPitch = 12f,
        };

        static Scenario ArrestedFloor() => new Scenario
        {
            id = "arrested",
            title = "4b · Partial attachment → arrested",
            instruction = "Press T to knock out both props. The floor hinges down onto the low wall.",
            expected = "The floor rotates until it lands on the low wall, then stops: hinge moment and residual q drop, damage stops growing, and it stays partially attached.",
            build = () => HangingFloorGeometry(true),
            highlight = new[] { "Prop L", "Prop R" },
            triggerLabel = "Knock out props",
            trigger = KnockProps,
            cameraPivot = new Vector3(0f, 2f, 2.5f), cameraDistance = 14f, cameraYaw = -60f, cameraPitch = 15f,
        };

        // ------------------------------------------------------------------ 5: cascade

        /// <summary>A tower wall with three stacked cantilever balconies, 3 m apart.</summary>
        static List<PieceDef> BalconyTower()
        {
            var list = new List<PieceDef>
            {
                PieceDef.Box("Tower wall", new Vector3(0f, 5.5f, -0.25f), new Vector3(4f, 11f, 0.5f), PieceKind.Wall),
            };
            for (int i = 0; i < 3; i++)
            {
                float y = 3f + i * 3f;
                list.Add(PieceDef.Box($"Balcony {3 - i}", new Vector3(0f, y + 0.125f, 1f), new Vector3(3f, 0.25f, 2f), PieceKind.Slab));
            }
            return list;
        }

        static Scenario SecondaryCollapse() => new Scenario
        {
            id = "cascade",
            title = "5 · Secondary collapse",
            instruction = "Press T to cut the top balcony (Balcony 1) from the tower.",
            expected = "Balcony 1 drops onto Balcony 2; the impact breaks Balcony 2's joint, which hinges, tears under the extra weight and falls onto Balcony 3, and so on. A bounded chain of 'impact' and 'residual-joint' failures; the log goes quiet once the rubble settles.",
            build = BalconyTower,
            highlight = new[] { "Balcony 1" },
            triggerLabel = "Cut top balcony",
            trigger = w => w.Sever(ConnectionsOf(w, "Balcony 1")),
            cameraPivot = new Vector3(0f, 5f, 1f), cameraDistance = 20f, cameraYaw = -55f, cameraPitch = 12f,
        };

        // ------------------------------------------------------------------ 6: rubble

        static Scenario PersistentRubble() => new Scenario
        {
            id = "rubble",
            title = "6 · Persistent rubble",
            instruction = "Wait for the pile to settle (bodies turn blue in Physics tint, F2). Press T to drop a 4 t block on it, or use Drop block.",
            expected = "Settled rubble sleeps but keeps full collision. The block lands on the pile, wakes the pieces it touches, is supported, and everything goes back to sleep.",
            build = () =>
            {
                var list = new List<PieceDef>();
                var rng = new System.Random(1234); // controlled randomness: same pile every reset
                int n = 0;
                for (int layer = 0; layer < 3; layer++)
                {
                    int count = 5 - layer * 2; // 5, 3, 1 pieces: a mound, not a tower
                    for (int i = 0; i < count; i++, n++)
                    {
                        float spread = 1.6f - layer * 0.6f;
                        float x = (float)(rng.NextDouble() * 2.0 - 1.0) * spread;
                        float z = (float)(rng.NextDouble() * 2.0 - 1.0) * spread;
                        var size = new Vector3(1.4f + (float)rng.NextDouble() * 0.8f, 0.25f + (float)rng.NextDouble() * 0.15f, 1.2f + (float)rng.NextDouble() * 0.6f);
                        // Distinct drop heights so pieces never start interpenetrating; they settle into a mound.
                        var d = PieceDef.Box($"Rubble {n}", new Vector3(x, 0.5f + n * 0.8f, z), size, PieceKind.Rubble);
                        d.loose = true;
                        d.rotation = Quaternion.Euler((float)rng.NextDouble() * 20f - 10f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 20f - 10f);
                        list.Add(d);
                    }
                }
                return list;
            },
            triggerLabel = "Drop 2 t block",
            trigger = w => w.DropBlock(new Vector3(0.1f, 4.5f, 0.1f), 2000f, 0f),
            cameraPivot = new Vector3(0f, 1f, 0f), cameraDistance = 11f, cameraYaw = -40f, cameraPitch = 25f,
        };
    }
}
