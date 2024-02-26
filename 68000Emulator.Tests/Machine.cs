namespace PendleCodeMonkey.MC68000Emulator.Tests
{
    internal class Machine : MC68000EmulatorLib.Machine
    {
        public Machine(Memory memory) : base(memory)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Machine"/> class.
        /// </summary>
        /// <param name="memorySize">The size (in bytes) of memory to be allocated for the emulator [optional].</param>
        public Machine(uint? memorySize = null) : this(new Memory(memorySize ?? _memorySize))
        {
        }

        /// <summary>
        /// Expose the test helper version of memory that allows internal access of the Data property.
        /// </summary>
        internal new Memory Memory
        {
            get { return base.Memory as Memory; }
        }

        internal new MC68000EmulatorLib.CPU CPU
        {
            get { return base.CPU; }
        }

        /// <summary>
        /// Gets a value indicating if the machine has reached the end of the loaded executable data.
        /// </summary>
        internal new bool IsEndOfData
        {
            get { return base.IsEndOfData; }
        }

        /// <summary>
        /// Gets a value indicating if the execution of code has been terminated.
        /// </summary>
        /// <remarks>
        /// This can occur when a RET instruction has been executed that was not within a subroutine invoked
        /// via the CALL instruction (i.e. a RET instruction intended to mark the end of execution).
        /// </remarks>
        internal new bool IsEndOfExecution
        {
            get { return base.IsEndOfExecution; }
        }

        /// <summary>
        /// Gets a value indicating if the execution of code has been stopped by a STOP instruction.
        /// </summary>
        internal new bool ExecutionStopped
        {
            get { return base.ExecutionStopped; }
        }

        /// <summary>
        /// Gets the <see cref="InstructionDecoder"/> instance used by this machine.
        /// </summary>
        /// <remarks>
        /// This instance decodes byte data into 68000 instructions.
        /// </remarks>
        internal new InstructionDecoder Decoder { get { return base.Decoder; } }

        /// <summary>
        /// Gets the <see cref="OpcodeExecutionHandler"/> instance used by this machine.
        /// </summary>
        /// <remarks>
        /// This instance performs the execution of the 68000 instructions.
        /// </remarks>
        internal new OpcodeExecutionHandler ExecutionHandler { get { return base.ExecutionHandler; } }

        /// <summary>
        /// Push a 32-bit value onto the stack.
        /// </summary>
        /// <param name="value">The 32-bit value to be pushed onto the stack.</param>
        internal new void PushLong(uint value)
        {
            base.PushLong(value);
        }

        /// <summary>
        /// Push a 16-bit value onto the stack.
        /// </summary>
        /// <param name="value">The 16-bit value to be pushed onto the stack.</param>
        internal new void PushWord(ushort value)
        {
            base.PushWord(value);
        }

        /// <summary>
        /// Pop a 32-bit value from the top of the stack.
        /// </summary>
        /// <returns>The 32-bit value popped off the top of the stack.</returns>
        internal new uint PopLong()
        {
            return base.PopLong();
        }

        /// <summary>
        /// Pop a 16-bit value from the top of the stack.
        /// </summary>
        /// <returns>The 16-bit value popped off the top of the stack.</returns>
        internal new ushort PopWord()
        {
            return base.PopWord();
        }
    }
}
