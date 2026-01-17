using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Debug interface for the emulator.
    /// </summary>
    public interface IDebugger
    {
        /// <summary>
        /// True if debugging.  Used to enable callbacks to <see cref="DebugReadAccess(uint)"/> 
        /// and <see cref="DebugWriteAccess(uint)"/>.
        /// </summary>
        bool Debugging { get; }

        /// <summary>
        /// True if disassembling. Can be used to override access checks,
        /// even address limitations, etc.
        /// </summary>
        bool PassiveAccess { get; set; }

        /// <summary>
        /// Callback to debugger on read access when Debugging.  Used for implementing
        /// watchpoints in the debugger.
        /// </summary>
        void DebugReadAccess(uint address);

        /// <summary>
        /// Callback to debugger on write access when Debugging.  Used for implementing
        /// watchpoints in the debugger.
        /// </summary>
        void DebugWriteAccess(uint address);

        /// <summary>
        /// Callback to debugger if a Status Register change may have happened.
        /// </summary>
        /// <param name="prevSr"></param>
        /// <param name="currSR"></param>
        void DebugStatusRegisterChange(SRFlags prevSr, SRFlags currSR);

        /// <summary>
        /// Callback to debugger to allow UI events during long operations
        /// like disassembling a large block of code.
        /// </summary>
        void DoEvents();

        /// <summary>
        /// Set to true by debugger to stop a long-running operation like
        /// disassembling a large block of memory.
        /// </summary>
        bool Cancelling { get; }
    }

    public static class DebugUtil
    {
        public static string GetCallerInfo(
                        [CallerMemberName] string memberName = "",
                        [CallerFilePath]   string sourceFilePath = "",
                        [CallerLineNumber] int sourceLineNumber = 0)
        {
            return $"File: {Path.GetFileName(sourceFilePath)}, Member: {memberName}, Line: {sourceLineNumber}";
        }

        /// <summary>
        /// Name of the caller of the method that called this method.
        /// 
        /// </summary>
        /// <param name="level">
        /// -1 - Caller of this method.
        ///  0 - Caller of caller of this method (normal usage).
        ///  1 - Caller of caller of caller of this method.
        ///  2 - Etc.
        /// </param>
        /// <returns></returns>
        [RequiresUnreferencedCode("Caller uses StackTrace and reflection, which may not be compatible with trimming.")]
        public static string Caller(int level = 0)
        {
            try
            {
                level += 2;
                var stackTrace = new System.Diagnostics.StackTrace();
                var frame = stackTrace.GetFrame(level); // Get the caller's frame
                if (frame != null)
                {
                    var method = frame.GetMethod();
                    return $"{method?.DeclaringType?.Name}.{method?.Name}";
                }
            }
            catch
            {
                // If we can't get the caller, return "Unknown"
            }
            return "Unknown";
        }
    }
}
