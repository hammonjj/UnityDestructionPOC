using System.Linq;
using UnityEngine;

namespace DestructionLab.Tests
{
    /// <summary>Builds a world with a ground plane and steps it deterministically (SimulationMode.Script).</summary>
    public sealed class WorldFixture
    {
        public DestructionWorld world;
        GameObject ground;
        GameObject worldGo;

        public static WorldFixture Create(string scenarioId) => Create(ScenarioLibrary.ById(scenarioId));

        public static WorldFixture Create(Scenario scenario)
        {
            var f = new WorldFixture();
            Time.fixedDeltaTime = 0.02f;
            f.ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            f.ground.name = "Ground";
            f.ground.transform.position = new Vector3(0f, -0.5f, 0f);
            f.ground.transform.localScale = new Vector3(200f, 1f, 200f);
            f.worldGo = new GameObject("World");
            f.world = f.worldGo.AddComponent<DestructionWorld>();
            f.world.Paused = true;
            f.world.Build(scenario);
            return f;
        }

        public void Steps(int n)
        {
            for (int i = 0; i < n; i++) world.Step();
        }

        public void Seconds(float s) => Steps(Mathf.CeilToInt(s / Time.fixedDeltaTime));

        public int Piece(string name) => world.Graph.pieces.FindIndex(p => p.name == name);

        public Transform PieceTransform(string name) => world.pieces[Piece(name)].transform;

        public Connection Between(string a, string b)
        {
            int ia = Piece(a);
            return b == "ground" ? world.Graph.GroundConnection(ia) : world.Graph.Find(ia, Piece(b));
        }

        public void Trigger() => world.CurrentScenario.trigger(world);

        public string LogText()
        {
            var lines = Enumerable.Range(0, world.log.Count).Select(i => world.log[i].ToString());
            return string.Join("\n", lines);
        }

        public void Dispose()
        {
            if (worldGo != null) Object.DestroyImmediate(worldGo);
            if (ground != null) Object.DestroyImmediate(ground);
            Physics.simulationMode = SimulationMode.FixedUpdate;
        }
    }
}
