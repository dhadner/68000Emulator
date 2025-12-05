using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;
using System.Text;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Implementation of the <see cref="Machine"/> class.
    /// Includes <see cref="OpcodeExecutionHandler"/>, <see cref="InstructionDecoder"/>, and <see cref="SRecordLoader"/> classes so they
    /// can access protected members that used to be internal but now need to be available to subclasses in other assemblies.
    /// </summary>
    public partial class Machine
    {
        internal const uint MAX_MEMORY_SIZE = 0x01000000;     // Default to 16MB of memory allocated for emulator (the max an actual 68000 processor can address).

        protected uint _loadedAddress;
        protected uint _dataLength;

        /// <summary>
        /// Single lock object for entire machine (==> no deadlocks)
        /// </summary>
        public static System.Threading.Lock MachineLock { get; } = new();

        /// <summary>
        /// True when loading a file into memory.
        /// </summary>
        public bool LoadingProgram { get; protected set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Machine"/> class.  Allows subclasses to
        /// use their own Memory implementation.
        /// </summary>
        /// <param name="memory"></param>
        public Machine(Memory memory)
        {
            CPU = new CPU();
            Memory = memory;
            CurrentInstruction = new Instruction(0, new InstructionInfo(0, 0, "NONE", Enumerations.OpHandlerID.NONE));
            ExecutionHandler = new OpcodeExecutionHandler(this);
            Decoder = new InstructionDecoder(this);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Machine"/> class.
        /// </summary>
        /// <param name="memorySize">The size (in bytes) of memory to be allocated for the emulator [optional].</param>
        public Machine(uint? memorySize = null) : this(new Memory(memorySize ?? MAX_MEMORY_SIZE))
        {
        }

        /// <summary>
        /// Gets the <see cref="CPU"/> instance used by this machine.
        /// </summary>
        protected CPU CPU { get; private set; }

        /// <summary>
        /// Gets the <see cref="Memory"/> instance used by this machine.
        /// </summary>
        protected Memory Memory { get; private set; }

        /// <summary>
        /// Gets the <see cref="InstructionDecoder"/> instance used by this machine.
        /// </summary>
        /// <remarks>
        /// This instance decodes byte data into 68000 instructions.
        /// </remarks>
        protected InstructionDecoder Decoder { get; private set; }

        /// <summary>
        /// Gets the <see cref="OpcodeExecutionHandler"/> instance used by this machine.
        /// </summary>
        /// <remarks>
        /// This instance performs the execution of the 68000 instructions.
        /// </remarks>
        internal OpcodeExecutionHandler ExecutionHandler { get; set; }

        /// <summary>
        /// Currently-executing instruction.
        /// </summary>
        /// 
        protected Instruction CurrentInstruction { get; set; }

        /// <summary>
        /// Gets a value indicating if the machine has reached the end of the loaded executable data.
        /// </summary>
        public virtual bool IsEndOfData => CPU.PC >= _loadedAddress + _dataLength;

        /// <summary>
        /// Gets a value indicating if the execution of code has been terminated.
        /// </summary>
        /// <remarks>
        /// This can occur when a RET instruction has been executed that was not within a subroutine invoked
        /// via the CALL instruction (i.e. a RET instruction intended to mark the end of execution).
        /// </remarks>
        public virtual bool IsEndOfExecution { get; protected set; }

        /// <summary>
        /// Optional debugger.  Subclasses can provide debug features.
        /// </summary>
        public IDebugger? Debugger { get; set; } = null;

        /// <summary>
        /// Configuration Option: True if machine should
        /// end execution when RTS call depth reaches 0.
        /// </summary>
        public bool EndWhenCallDepthIsZero { get; set; } = true;

        /// <summary>
        /// Gets a value indicating if the execution of code has been stopped by a STOP instruction.
        /// </summary>
        public virtual bool ExecutionStopped { get; protected set; }

        /// <summary>
        /// Reset the machine to its default state.
        /// </summary>
        public virtual void Reset(bool initializing = false)
        {
            Memory.Clear();
            CPU.Reset(initializing);
            IsEndOfExecution = false;
            ExecutionStopped = false;
            _loadedAddress = 0;
            _dataLength = 0;
        }

        /// <summary>
        /// Dumps the current state of the machine in a string format.
        /// </summary>
        /// <returns>A string containing details of the current state of the machine.</returns>
        public string Dump()
        {
            StringBuilder sb = new();

            sb.Append($"D0: 0x{CPU.ReadDataRegister(0):X4} ({CPU.ReadDataRegister(0)})");
            sb.Append(Environment.NewLine);
            sb.Append($"D1: 0x{CPU.ReadDataRegister(1):X4} ({CPU.ReadDataRegister(1)})");
            sb.Append(Environment.NewLine);
            sb.Append($"D2: 0x{CPU.ReadDataRegister(2):X4} ({CPU.ReadDataRegister(2)})");
            sb.Append(Environment.NewLine);
            sb.Append($"D3: 0x{CPU.ReadDataRegister(3):X4} ({CPU.ReadDataRegister(3)})");
            sb.Append(Environment.NewLine);
            sb.Append($"D4: 0x{CPU.ReadDataRegister(4):X4} ({CPU.ReadDataRegister(4)})");
            sb.Append(Environment.NewLine);
            sb.Append($"D5: 0x{CPU.ReadDataRegister(5):X4} ({CPU.ReadDataRegister(5)})");
            sb.Append(Environment.NewLine);
            sb.Append($"D6: 0x{CPU.ReadDataRegister(6):X4} ({CPU.ReadDataRegister(6)})");
            sb.Append(Environment.NewLine);
            sb.Append($"D7: 0x{CPU.ReadDataRegister(7):X4} ({CPU.ReadDataRegister(7)})");
            sb.Append(Environment.NewLine);
            sb.Append($"A0: 0x{CPU.ReadAddressRegister(0):X4} ({CPU.ReadAddressRegister(0)})");
            sb.Append(Environment.NewLine);
            sb.Append($"A1: 0x{CPU.ReadAddressRegister(1):X4} ({CPU.ReadAddressRegister(1)})");
            sb.Append(Environment.NewLine);
            sb.Append($"A2: 0x{CPU.ReadAddressRegister(2):X4} ({CPU.ReadAddressRegister(2)})");
            sb.Append(Environment.NewLine);
            sb.Append($"A3: 0x{CPU.ReadAddressRegister(3):X4} ({CPU.ReadAddressRegister(3)})");
            sb.Append(Environment.NewLine);
            sb.Append($"A4: 0x{CPU.ReadAddressRegister(4):X4} ({CPU.ReadAddressRegister(4)})");
            sb.Append(Environment.NewLine);
            sb.Append($"A5: 0x{CPU.ReadAddressRegister(5):X4} ({CPU.ReadAddressRegister(5)})");
            sb.Append(Environment.NewLine);
            sb.Append($"A6: 0x{CPU.ReadAddressRegister(6):X4} ({CPU.ReadAddressRegister(6)})");
            sb.Append(Environment.NewLine);
            sb.Append($"A7: 0x{CPU.ReadAddressRegister(7):X4} ({CPU.ReadAddressRegister(7)})");
            sb.Append(Environment.NewLine);
            sb.Append($"USP: 0x{CPU.USP:X4} ({CPU.USP})");
            sb.Append(Environment.NewLine);
            sb.Append($"SSP: 0x{CPU.SSP:X4} ({CPU.SSP})");
            sb.Append(Environment.NewLine);
            sb.Append($"PC: 0x{CPU.PC:X4} ({CPU.PC})");
            sb.Append(Environment.NewLine);
            sb.Append($"Status Register: 0x{(int)CPU.SR:X2} ({CPU.SR})");
            sb.Append(Environment.NewLine);

            return sb.ToString();
        }

        /// <summary>
        /// Get a <see cref="CPUState"/> object containing the current CPU state settings (i.e. register
        /// values, Program Counter, Stack Pointer, etc.)
        /// </summary>
        /// <returns>A <see cref="CPUState"/> object containing the current CPU state settings.</returns>
        public CPUState GetCPUState()
        {
            CPUState state = new();
            state.FromCPU(CPU);
            return state;
        }

        /// <summary>
        /// Transfer the state from the CPU to the <see cref="CPUState"/> object. Used to minimize
        /// new object creation/GC loading.
        /// </summary>
        /// <param name="state">A <see cref="CPUState"/> object that will receive the current CPU state settings.</param>
        public void GetCPUState(ref CPUState state)
        {
            state.FromCPU(CPU);
        }

        /// <summary>
        /// Get the current CPU control state.
        /// </summary>
        /// <returns></returns>
        public (uint pc, SRFlags sr) GetCPUControlState()
        {
            return (CPU.PC, CPU.SR);
        }

        /// <summary>
        /// Set the state of settings in the CPU according to the values in the supplied <see cref="CPUState"/> object.
        /// </summary>
        /// <remarks>
        /// Only non-null values in the supplied <see cref="CPUState"/> instance will be transferred to the CPU, all other
        /// CPU settings will be unaffected.
        /// </remarks>
        /// <param name="state">A <see cref="CPUState"/> object containing the new CPU state settings.</param>
        public virtual void SetCPUState(CPUState state)
        {
            state.ToCPU(CPU);
        }

        /// <summary>
        /// Get the current call depth to support debugging (step-out, step-over).
        /// </summary>
        public int CallDepth => ExecutionHandler._numberOfJSRCalls;

        /// <summary>
        /// Load executable data into memory at the specified address.
        /// </summary>
        /// <remarks>
        /// Loading executable data also sets the Program Counter to the address of the loaded data.
        /// </remarks>
        /// <param name="data">The executable data to be loaded.</param>
        /// <param name="loadAddress">The address at which the executable data should be loaded.</param>
        /// <param name="clearBeforeLoad"><c>true</c> if all memory should be cleared prior to loading the data, otherwise <c>false</c>.</param>
        /// <returns><c>true</c> if the executable data was successfully loaded, otherwise <c>false</c>.</returns>
        public bool LoadExecutableData(byte[] data, uint loadAddress, bool clearBeforeLoad = true)
        {
            if (Memory.LoadData(data, loadAddress, clearBeforeLoad))
            {
                CPU.PC = loadAddress;
                _loadedAddress = loadAddress;
                _dataLength = (uint)data.Length;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Load executable data into memory at the specified address.
        /// </summary>
        /// <remarks>
        /// Loading executable data also sets the Program Counter to the address of the loaded data.
        /// </remarks>
        /// <param name="data">The 16-bit executable data to be loaded.</param>
        /// <param name="loadAddress">The address at which the executable data should be loaded.</param>
        /// <param name="clearBeforeLoad"><c>true</c> if all memory should be cleared prior to loading the data, otherwise <c>false</c>.</param>
        /// <returns><c>true</c> if the executable data was successfully loaded, otherwise <c>false</c>.</returns>
        public bool LoadExecutableData(ushort[] data, uint loadAddress, bool clearBeforeLoad = true)
        {
            var bData = ToByteArray(data);
            if (Memory.LoadData(bData, loadAddress, clearBeforeLoad))
            {
                CPU.PC = loadAddress;
                _loadedAddress = loadAddress;
                _dataLength = (uint)bData.Length;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Load non-executable data into memory at the specified address.
        /// </summary>
        /// <param name="data">The data to be loaded.</param>
        /// <param name="loadAddress">The address at which the data should be loaded.</param>
        /// <param name="clearBeforeLoad"><c>true</c> if all memory should be cleared prior to loading the data, otherwise <c>false</c>.</param>
        /// <returns><c>true</c> if the data was successfully loaded, otherwise <c>false</c>.</returns>
        public bool LoadData(byte[] data, uint loadAddress, bool clearBeforeLoad = true)
        {
            return Memory.LoadData(data, loadAddress, clearBeforeLoad);
        }

        /// <summary>
        /// Load 16-bit non-executable data into memory at the specified address.
        /// </summary>
        /// <param name="data">The 16-bit data to be loaded.</param>
        /// <param name="loadAddress">The address at which the data should be loaded.</param>
        /// <param name="clearBeforeLoad"><c>true</c> if all memory should be cleared prior to loading the data, otherwise <c>false</c>.</param>
        /// <returns><c>true</c> if the data was successfully loaded, otherwise <c>false</c>.</returns>
        public bool LoadData(ushort[] data, uint loadAddress, bool clearBeforeLoad = true)
        {
            return Memory.LoadData(ToByteArray(data), loadAddress, clearBeforeLoad);
        }

        /// <summary>
        /// Load from an S-Record file.
        /// </summary>
        /// <param name="sFile">S-Record executable file path</param>
        /// <param name="patch"><c>true</c> to prevent clearing all memory before loading
        /// and resetting memory limits and initial PC</param>
        /// <returns><c>null</c> if memory loaded successfully, error message if load error occurred</returns>
        public string? LoadProgram(string sFile, bool patch = false)
        {
            if (!patch)
            {
                Memory.Clear();
            }
            SRecordLoader loader = new(this);
            LoadingProgram = true;
            string? errMsg = loader.Load(sFile, out uint? startingAddress, out uint lowestAddress, out uint highestAddress);
            LoadingProgram = false;
            if (!patch && errMsg == null)
            {
                if (startingAddress.HasValue)
                {
                    CPU.PC = startingAddress.Value;
                }
                _loadedAddress = lowestAddress;
                _dataLength = highestAddress - lowestAddress;
            }
            return errMsg;
        }

        /// <summary>
        /// Convert the supplied 16-bit data array into an array of 8-bit values.
        /// </summary>
        /// <param name="data">The 16-bit data array.</param>
        /// <returns>The resulting array of 8-bit values.</returns>
        private static byte[] ToByteArray(ushort[] data)
        {
            byte[] bData = new byte[data.Length * 2];

            for (int i = 0; i < data.Length; i++)
            {
                bData[i * 2] = (byte)((data[i] & 0xFF00) >> 8);
                bData[(i * 2) + 1] = (byte)(data[i] & 0x00FF);
            }

            return bData;
        }

        /// <summary>
        /// Return the specified block of memory.
        /// </summary>
        /// <param name="address">Start address of the requested block of memory.</param>
        /// <param name="length">Length (in bytes) of the block of memory to be retrieved.</param>
        /// <returns>Read-only copy of the requested memory.</returns>
        public ReadOnlySpan<byte> DumpMemory(uint address, uint length)
        {
            return Memory.DumpMemory(address, length);
        }

        /// <summary>
        /// Push a 32-bit value onto the stack.
        /// </summary>
        /// <param name="value">The 32-bit value to be pushed onto the stack.</param>
        protected void PushLong(uint value)
        {
            uint stack = CPU.ReadAddressRegister(7);
            stack -= 4;
            CPU.WriteAddressRegister(7, stack);
            Memory.WriteLong(stack, value);
        }

        /// <summary>
        /// Push a 16-bit value onto the stack.
        /// </summary>
        /// <param name="value">The 16-bit value to be pushed onto the stack.</param>
        protected void PushWord(ushort value)
        {
            uint stack = CPU.ReadAddressRegister(7);
            stack -= 2;
            CPU.WriteAddressRegister(7, stack);
            Memory.WriteWord(stack, value);
        }

        /// <summary>
        /// Pop a 32-bit value from the top of the stack.
        /// </summary>
        /// <returns>The 32-bit value popped off the top of the stack.</returns>
        protected uint PopLong()
        {
            uint stack = CPU.ReadAddressRegister(7);
            uint value = Memory.ReadLong(stack);
            stack += 4;
            CPU.WriteAddressRegister(7, stack);
            return value;
        }

        /// <summary>
        /// Pop a 16-bit value from the top of the stack.
        /// </summary>
        /// <returns>The 16-bit value popped off the top of the stack.</returns>
        protected ushort PopWord()
        {
            uint stack = CPU.ReadAddressRegister(7);
            ushort value = Memory.ReadWord(stack);
            stack += 2;
            CPU.WriteAddressRegister(7, stack);
            return value;
        }

        /// <summary>
        /// Start executing instructions from the current Program Counter address.
        /// </summary>
        /// <remarks>
        /// This method keeps executing instructions until a TrapException occurs
        /// or until IsEndOfData, IsEndOfExecution, or ExecutionStopped occurs.
        /// It returns after handling the TrapException.
        /// </remarks>
        /// <exception cref="TrapException"/>
        public virtual void ExecuteUntilException()
        {
            TrapException? exception;
            while (!IsEndOfData && !IsEndOfExecution && !ExecutionStopped)
            {
                exception = ExecuteInstruction();
                if (exception != null) throw exception;
            }
        }

        /// <summary>
        /// Execute until end of data, end of execution, or execution stopped.
        /// </summary>
        public virtual void Execute()
        {
            while (!IsEndOfData && !IsEndOfExecution && !ExecutionStopped)
            {
                ExecuteInstruction();
            }
        }

        /// <summary>
        /// Execute a single instruction located at the current Program Counter address.
        /// </summary>
        /// <returns>null or TrapException</returns>
        /// <exception cref="IllegalInstruction">Illegal Instructions not returned, thrown
        /// instead</exception>
        /// <exception cref="TrapException">Some TrapExceptions are thrown for low-level bus
        /// errors, odd address access by word instructions, etc. Most TrapExceptions expected 
        /// during normal operation (e.g., LINEA and machine interrupts) are returned by this 
        /// routine to the caller for handling without the overhead of .NET stack frame 
        /// crawling, etc.</exception>
        public virtual TrapException? ExecuteBareInstruction()
        {
            var instruction = Decoder.FetchInstruction();
            if (instruction == null)
            {
                return Helpers.CreateTRAPException(TrapVector.IllegalInstruction);
            }
            return ExecutionHandler.Execute(instruction);
        }

        public virtual TrapException? ExecuteInstruction()
        {
            TrapException? exception = null;
            InstructionCount++;
            bool traceMode = (CurrentSR & SRFlags.TraceMode) != 0;

            try
            {
                exception = ExecuteBareInstruction();
                if (exception != null)
                {
                    traceMode = false;
                    HandleTrapException(exception);
                }
            }
            catch (TrapException te)
            {
                // Handle any missed by the opcode handlers or generated by low-level I/O
                traceMode = false;
                HandleTrapException(te);
            }
            if (traceMode)
            {
                HandleTrapException(new TrapException((ushort)TrapVector.Trace));
            }
            (var interruptException, var _) = HandleInterrupt();

            // CurrentPC for the next instruction.
            (CurrentPC, CurrentSR) = GetCPUControlState();

            return exception ?? interruptException;
        }

        /// <summary>
        /// Stop instruction execution (as a result of executing a STOP instruction).
        /// </summary>
        internal void StopExecution()
        {
            ExecutionStopped = true;
        }

        /// <summary>
        /// Set Function Code (FC) outputs.
        /// </summary>
        /// <param name="newFc"></param>
        public virtual void SetFCOutputs(FC newFc)
        {
        }

        /// <summary>
        /// Count of instructions executed since Reset.
        /// </summary>
        public ulong InstructionCount { get; set; }


        /// <summary>
        /// Current IPL.
        /// </summary>
        public virtual byte IPL { get; protected set; }

        /// <summary>
        /// Current Function Code (FC) outputs.
        /// </summary>
        public virtual FC Fc { get; protected set; } = FC.SupervisorProgram;

        /// <summary>
        /// Current Program Counter at beginning of instruction.
        /// </summary>
        public uint CurrentPC { get; protected set; } = 0;

        /// <summary>
        /// Current Status Register at the beginning of instruction.
        /// </summary>
        public SRFlags CurrentSR { get; protected set; } = 0;

        /// <summary>
        /// Singleton object to minimize garbage collection.
        /// </summary>
        protected CPUState TrapCPUState = new();

        /// <summary>
        /// True if there is an interrupt pending.  Set to false
        /// by the machine interrupt handler when it is either processed
        /// or ignored due to interrupt masking.  Stays high until
        /// interrupt acknowledge: interrupt acknowledge (FC2–FC0 and A19–A16 high).
        /// </summary>
        public bool InterruptPending { get; set; } = false;

        /// <summary>
        /// Past value of <see cref="InterruptPending"/>.
        /// </summary>
        public bool InterruptPendingPV { get; set; } = false;

        /// <summary>
        /// Called when IPL pins change.  Sets
        /// <see cref="InterruptPending"/> if the new
        /// IPL is different from the old IPL and is non-zero.
        /// </summary>
        public virtual void IPLChangeNotify(byte newIPL)
        {
            if (newIPL != IPL)
            {
                InterruptPending = newIPL != 0;
            }
        }

        /// <summary>
        /// Process external interrupt if present. 
        /// Acknowledge interrupt (FC2–FC0 and A19–A16 high).
        /// </summary>
        /// <returns>true if an interrupt was pending</returns>
        protected virtual (TrapException? exception, bool handled) HandleInterrupt()
        {
            if (InterruptPendingPV && !InterruptPending)
            {
                // Clear FC outputs.
                SetFCOutputs(FC.Zero);
            }
            InterruptPending = IPL != 0;
            InterruptPendingPV = InterruptPending;
            if (!InterruptPending)
            {
                return (null, false);
            }

            // Interrupt is pending, clear the flag for next time.
            InterruptPending = false;
            GetCPUState(ref TrapCPUState);

            byte interruptMask = (byte)(((ushort)TrapCPUState.SR!.Value & 0x0700) >> 8);
            byte vector = (byte)((int)TrapVector.Interrupt + IPL - 1);
            TrapException interruptException = new(vector);
            bool handled = IPL == 7 || IPL > interruptMask;
            if (handled)
            {
                HandleTrapException(interruptException);

                // Acknowledge interrupt to devices.
                SetFCOutputs(FC.CPUSpace);
                Memory.ReadByte(0x000f0000);
            }
            return (interruptException, handled);
        }

        /// <summary>
        /// Handle a trap exception.
        /// </summary>
        /// <param name="te"></param>
        protected void HandleTrapException(TrapException te)
        {
            var eVEntry = EVEntry.FromVector((TrapVector)te.Vector);
            if (eVEntry == null)
            {
                Logger.Log(LogLevel.Error, "CPU", () => $"Unknown Trap Exception: {te}");
                return;
            }

            GetCPUState(ref TrapCPUState);
            uint nextInstructionPC = TrapCPUState.PC!.Value;
            uint oldPC = CurrentPC;
            SRFlags oldSR = TrapCPUState.SR!.Value;

            uint trapVectorContents = Memory.ReadLong((uint)te.Vector * 4) & 0x00ffffff;
            TrapCPUState.PC = trapVectorContents;
            TrapCPUState.SR |= SRFlags.SupervisorMode;
            TrapCPUState.SR &= ~SRFlags.TraceMode;
            if (eVEntry.Value.IsInterrupt)
            {
                // Set priority = interrupt priority
                TrapCPUState.SR &= ~SRFlags.InterruptLevel;
                ushort level = (ushort)(te.Vector - (byte)TrapVector.Interrupt + 1);
                TrapCPUState.SR |= (SRFlags)(level << 8);
            }
            SetCPUState(TrapCPUState);

            switch (eVEntry.Value.Group)
            {
                case EG.Group0:
                    Logger.Log(LogLevel.Debug, "CPU", () => $"Group 0 Trap Exception handled: {te.Vector} Trap handler: {trapVectorContents:x8} Called from: {oldPC:x8}");

                    ushort fcWord = (ushort)eVEntry.Value.Fc;
                    fcWord |= 0x0018;               // Default to read and not an instruction
                    PushLong(nextInstructionPC);
                    PushWord((ushort)oldSR);
                    PushWord(CurrentInstruction.Opcode);
                    if (CurrentInstruction.AccessAddress.HasValue)
                    {
                        PushLong(CurrentInstruction.AccessAddress.Value);
                        if (CurrentInstruction.AccessAddressType.HasValue && CurrentInstruction.AccessAddressType.Value == EAType.Destination)
                        {
                            fcWord &= 0xffef; // Clear the "read" bit
                        }
                    }
                    else
                    {
                        PushLong(0);
                    }
                    PushWord(fcWord);
                    break;
                case EG.Group1:
                    Logger.Log(LogLevel.Debug, "CPU", () => $"Group 1 Trap Exception handled: {te.Vector} Trap handler: {trapVectorContents:x8} Called from: {oldPC:x8}");
                    PushLong(nextInstructionPC);
                    PushWord((ushort)oldSR);
                    break;
                case EG.Group2:
                    Logger.Log(LogLevel.Debug, "CPU", () => $"Group 2 Trap Exception handled: {te.Vector} Trap handler: {trapVectorContents:x8} Called from: {oldPC:x8}");
                    PushLong(oldPC);
                    PushWord((ushort)oldSR);
                    break;
                default:
                    Logger.Log(LogLevel.Critical, "CPU", $"Trap exception handler error - error in EVTable.  Called from: {oldPC:x8}");
                    break;
            }
            SetFCOutputs(eVEntry.Value.Fc);

            if (trapVectorContents == 0)
            {
                Logger.Log(LogLevel.Critical, "CPU", () => $"Trap Exception  -> address = 0: {te}");
            }
            return;
        }
    }
}
