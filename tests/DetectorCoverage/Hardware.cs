using VRageMath;

namespace Sandbox.ModAPI.Interfaces
{
    public interface ITerminalProperty<T>
    {
        T GetValue(Sandbox.ModAPI.IMyTerminalBlock block);
        T GetMaximum(Sandbox.ModAPI.IMyTerminalBlock block);
    }
}

namespace Sandbox.ModAPI.Ingame
{
    public interface IMyConveyorSorter { bool Enabled { get; } }
}

namespace Sandbox.ModAPI
{
    public sealed class Grid { public long EntityId; }
    public sealed class Definition { public string SubtypeName; }
    public interface IMyTerminalBlock
    {
        Grid CubeGrid { get; }
        Definition BlockDefinition { get; }
        bool Closed { get; }
        bool MarkedForClose { get; }
        bool IsWorking { get; }
        bool HasPlayerAccess(long player);
        object GetProperty(string key);
        Vector3D GetPosition();
    }
    internal sealed class TestRange : Interfaces.ITerminalProperty<float>
    {
        public float Value = 150;
        public float GetValue(IMyTerminalBlock block) => Value;
        public float GetMaximum(IMyTerminalBlock block) => 24000;
    }
    internal class TestBlock : IMyTerminalBlock
    {
        public Grid CubeGrid { get; } = new Grid();
        public Definition BlockDefinition { get; } = new Definition();
        public bool Closed { get; set; }
        public bool MarkedForClose { get; set; }
        public bool IsWorking { get; set; } = true;
        public TestRange Slider = new TestRange();
        public TestBlock(string subtype, long grid)
        {
            BlockDefinition.SubtypeName = subtype;
            CubeGrid.EntityId = grid;
        }
        public bool HasPlayerAccess(long player) => player == 7;
        public object GetProperty(string key) => key == "Weapon Range" ? Slider : null;
        public Vector3D GetPosition() => new Vector3D(850.05, 1100, 1100);
    }
    internal sealed class TestSorter : TestBlock, Ingame.IMyConveyorSorter
    {
        public bool Enabled { get; set; } = true;
        public TestSorter(string subtype, long grid) : base(subtype, grid) { }
    }
}
