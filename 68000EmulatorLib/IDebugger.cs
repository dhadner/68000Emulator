namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Debug interface for the emulator.
    /// </summary>
    public interface IDebugger
    {
        /// <summary>
        /// True if debugging.
        /// </summary>
        bool Debugging { get; set; }

        /// <summary>
        /// True if disassembling. Can be used to override access checks,
        /// even address limitations, etc.
        /// </summary>
        bool Disassembling { get; set; }

        /// <summary>
        /// Callback to debugger on read access when Debugging.
        /// </summary>
        void DebugReadAccess(uint address);

        /// <summary>
        /// Callback to debugger on write access when Debugging.
        /// </summary>
        void DebugWriteAccess(uint address);
    }
}
