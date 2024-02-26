namespace PendleCodeMonkey.MC68000Emulator.Tests
{
    /// <summary>
    /// Helper class to get at protected Data member.
    /// </summary>
    internal class Memory : MC68000EmulatorLib.Memory
    {
        internal Memory(uint memSize) : base(memSize)
        {
        }
        internal new byte[] Data
        {
            get
            {
                return base.Data;
            }
            set
            {
                base.Data = value;
            }
        }
    }
}
