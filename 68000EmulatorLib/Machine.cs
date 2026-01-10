using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Implementation of the <see cref="Machine"/> class.
    /// Includes <see cref="OpcodeExecutionHandler"/>, <see cref="InstructionDecoder"/>, and <see cref="SRecordLoader"/> classes so they
    /// can access protected members that used to be internal but now need to be available to subclasses in other assemblies.
    /// </summary>
    [RequiresMachineThread]
    public partial class Machine
    {
        internal const uint MAX_MEMORY_SIZE = 0x01000000;        // Default to 16MB of memory allocated for emulator (the max an actual 68000 processor can address).
        public const ushort SR_IMPLEMENTED_BITS_68000 = 0xA71F;  // Only these bits are implemented in the SR for the 68000. Others always 0.

        protected uint _loadedAddress;
        protected uint _dataLength;

        /// <summary>
        /// Single lock object for entire machine (==> no deadlocks)
        /// </summary>
        public static System.Threading.Lock MachineLock { get; } = new();

        /// <summary>
        /// ManagedThreadId for machine thread.
        /// </summary>
        public static int? MachineThreadId { get; set; }

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
        /// Return true if the MachineThreadId has not been set or if the current thread is the machine thread.
        /// </summary>
        /// <returns></returns>
        public static bool IsMachineThread()
        {
            return MachineThreadId == null || MachineThreadId == Environment.CurrentManagedThreadId;
        }

        /// <summary>
        /// Fails assertion if not called from machine thread.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [RequiresMachineThread]
        public static void AssertIsMachineThread()
        {
#if DEBUG
            Debug.Assert(IsMachineThread());
#endif
        }

        /// <summary>
        /// Disassembler for logging purposes, etc.
        /// </summary>
        public Disassembler? Disasm { get; set; }

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
        /// Currently-executing instruction.  Equivalent to Instruction
        /// Decode Register (IDR) and associated logic.
        /// </summary>
        /// 
        public Instruction CurrentInstruction { get; protected set; }

        /// <summary>
        /// Address of the currently-executing (or about to be executed) instruction.
        /// </summary>
        public uint CurrentInstructionAddress { get; set; }

        /// <summary>
        /// Raise an address error trap exception and set appropriate info
        /// for Group 0 trap stack frame.
        /// </summary>
        /// <param name="eaType"></param>
        /// <param name="address"></param>
        public void RaiseAddressErrorException(EAType eaType, uint address)
        {
            Logger.Log(LogLevel.Error, "CPU", () => $"Address Error exception: address {address:X8}");
            CurrentInstruction.AccessAddress = address;
            CurrentInstruction.AccessAddressType = eaType;

            Helpers.RaiseTRAPException(TrapVector.AddressError);
        }

        /// <summary>
        /// Check for unaligned stack access.
        /// </summary>
        public void CheckUnalignedStackAccess(EAType eaType)
        {
            CheckUnalignedAccess(eaType, OpSize.Word, CPU.ReadAddressRegister(7));
        }

        /// <summary>
        /// Check for unaligned memory access.  Throw Address Error trap exception if
        /// not byte access and address is not even.
        /// </summary>
        /// <param name="eaType"></param>
        /// <param name="size"></param>
        /// <param name="address"></param>
        public void CheckUnalignedAccess(EAType eaType, OpSize size, uint address)
        {
            if ((address & 1) != 0)
            {
                CurrentInstruction.AccessAddress = address;
                CurrentInstruction.AccessAddressType = eaType;
                Helpers.RaiseTRAPException(TrapVector.AddressError);
            }
        }

        /// <summary>
        /// Gets a value indicating if the machine has reached the end of the loaded executable data.
        /// </summary>
        public virtual bool IsEndOfData => CPU.Prefetch.IsEmpty && CPU.PC >= _loadedAddress + _dataLength;

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
        /// Gets a value indicating if the execution of code has been stopped by a STOP instruction
        /// or double bus fault.
        /// </summary>
        public virtual bool ExecutionStopped { get; protected set; }

        /// <summary>
        /// Reset the machine to its default state.
        /// </summary>
        public virtual void Reset(bool initializing = false)
        {
            Memory.Clear();
            CPU.Reset(initializing);
            CurrentInstructionAddress = CPU.PC - CPU.Prefetch.Size;
            IsEndOfExecution = false;
            ExecutionStopped = false;
            Exception = null;
            InstructionCount = 0;
            _loadedAddress = 0;
            _dataLength = 0xffffffff;
        }

        /// <summary>
        /// Set load address and length manually.
        /// </summary>
        /// <param name="lowAddress"></param>
        /// <param name="length"></param>
        public virtual void SetExecutionLimits(uint lowAddress, uint length)
        {
            _loadedAddress = lowAddress;
            _dataLength = length;
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
            state.From(CPU);
            return state;
        }

        /// <summary>
        /// Transfer the state from the CPU to the <see cref="CPUState"/> object. Used to minimize
        /// new object creation/GC loading.
        /// </summary>
        /// <param name="state">A <see cref="CPUState"/> object that will receive the current CPU state settings.</param>
        public void GetCPUState(ref CPUState state)
        {
            state.From(CPU);
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
            if (state.PC.HasValue && state.Prefetch != null)
            {
                CurrentInstructionAddress = CPU.CurrentPC;
            }
        }

        /// <summary>
        /// Get the current call depth to support debugging (step-out, step-over).
        /// </summary>
        public int CallDepth {
            get => ExecutionHandler.CallDepth;
            set => ExecutionHandler.CallDepth = value;
        }

        /// <summary>
        /// Return the next word at the CurrentPC and increment the CurrentPC.
        /// Accesses the prefetch queue and refills as needed.
        /// </summary>
        /// <returns>The next word in prefetch queue at the CurrentPC address.</returns>
        internal ushort ReadNextPCWord()
        {
            /// <summary>
            /// Read word at the PC and increment the PC to the next word.
            /// </summary>
            /// <returns></returns>
            ushort ReadNextWord()
            {
                ushort value = Memory.ReadWord(CPU.PC);
                CPU.PC += 2;
                return value;
            }

            ushort value;

            if (CPU.Prefetch.Count == CPU.Prefetch.Capacity)
            {
                // Prefetch queue is full - normal case
                value = CPU.Prefetch.Dequeue();
                if (CPU.PC < _loadedAddress + _dataLength)
                {
                    CPU.Prefetch.Enqueue(ReadNextWord());
                }
            }
            else
            {
                if (CPU.Prefetch.IsEmpty)
                {
                    value = ReadNextWord();
                }
                else
                {
                    value = CPU.Prefetch.Dequeue();
                }
                // Prefetch queue is not full - initial filling of the queue
                while (CPU.Prefetch.Count < CPU.Prefetch.Capacity && !IsEndOfData)
                {
                    CPU.Prefetch.Enqueue(ReadNextWord());
                }
            }
            return value;
        }

        /// <summary>
        /// Ensure the prefetch queue is filled to capacity by fetching words from memory.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if attempting to read beyond the loaded data area.</exception>"
        /// <exception cref="TrapException">Thrown if a bus error occurs while reading memory.</exception>"
        public void FillPrefetch()
        {
            while (CPU.Prefetch.Count < CPU.Prefetch.Capacity && (CPU.PC & CPU.LEGAL_ADDRESS_MASK) < _loadedAddress + _dataLength)
            {
                ushort word = Memory.ReadWord(CPU.PC);
                CPU.Prefetch.Enqueue(word);
                CPU.PC += 2;
            }
        }

        /// <summary>
        /// Clear prefetch queue.
        /// </summary>
        public void ClearPrefetch()
        {
            CPU.Prefetch.Clear();
        }

        /// <summary>
        /// Set the PC to the specified value, clear the prefetch queue, and reload it.
        /// </summary>
        /// <param name="newPC"></param>
        /// <exception cref="TrapException">Thrown if an address error occurs while filling the prefetch queue.</exception>"
        public void SetPC(uint newPC)
        {
            CPU.CurrentPC = newPC;

            CurrentInstructionAddress = CPU.CurrentPC;
            CurrentInstruction.AccessAddress = newPC;
            CurrentInstruction.AccessAddressType = EAType.Source;

            Debug.Assert(newPC == CPU.CurrentPC);

            FillPrefetch(); // Throws address error if odd address
        }

        /// <summary>
        /// Return PC adjusted back for prefetch queue.
        /// </summary>
        /// <returns></returns>
        public uint GetPC()
        {
            return CPU.CurrentPC;
        }

        /// <summary>
        /// Load executable data into memory at the specified address.
        /// </summary>
        /// <remarks>
        /// Loading executable data also sets the Program Counter to the address of the loaded data
        /// and clears the prefetch queue.
        /// </remarks>
        /// <param name="data">The executable data to be loaded.</param>
        /// <param name="loadAddress">The address at which the executable data should be loaded.</param>
        /// <param name="clearBeforeLoad"><c>true</c> if all memory should be cleared prior to loading the data, otherwise <c>false</c>.</param>
        /// <returns><c>true</c> if the executable data was successfully loaded, otherwise <c>false</c>.</returns>
        public bool LoadExecutableData(byte[] data, uint loadAddress, bool clearBeforeLoad = true)
        {
            if (Memory.LoadData(data, loadAddress, clearBeforeLoad))
            {
                _loadedAddress = loadAddress;
                _dataLength = (uint)data.Length;
                SetPC(loadAddress);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Load executable data into memory at the specified address.
        /// </summary>
        /// <remarks>
        /// Loading executable data also sets the Program Counter to the address of the loaded data
        /// and then fills the prefetch queue (which also increments the PC).
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
                _loadedAddress = loadAddress;
                _dataLength = (uint)bData.Length;
                SetPC(loadAddress);
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
        /// Load from an S-Record file. Clears the prefetch queue.
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
                _loadedAddress = lowestAddress;
                _dataLength = highestAddress - lowestAddress;
                if (startingAddress.HasValue)
                {
                    SetPC(startingAddress.Value);
                }
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
            Memory.WriteLong(stack, value);
            CPU.WriteAddressRegister(7, stack);
        }

        /// <summary>
        /// Push long value to stack, return exception if bus or address error occurs.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        protected TrapException? PushLongCheck(uint value)
        {
            uint stack = CPU.ReadAddressRegister(7);
            try
            {
                PushLong(value);
                return null;
            }
            catch (TrapException e)
            {
                CPU.WriteAddressRegister(7, stack);
                return e;
            }
        }

        /// <summary>
        /// Push a 16-bit value onto the stack.
        /// </summary>
        /// <param name="value">The 16-bit value to be pushed onto the stack.</param>
        protected void PushWord(ushort value)
        {
            uint stack = CPU.ReadAddressRegister(7);
            stack -= 2;
            Memory.WriteWord(stack, value);
            CPU.WriteAddressRegister(7, stack);
        }

        /// <summary>
        /// Push long value to stack, return exception if bus or address error occurs.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        protected TrapException? PushWordCheck(ushort value)
        {
            uint stack = CPU.ReadAddressRegister(7);
            try
            {
                PushWord(value);
                return null;
            }
            catch (TrapException e)
            {
                CPU.WriteAddressRegister(7, stack);
                return e;
            }
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
        /// Check for exception and return if so.
        /// </summary>
        /// <returns></returns>
        protected (uint? value, TrapException? exception) PopLongCheck()
        {
            try
            {
                return (PopLong(), null);
            }
            catch (TrapException e)
            {
                return (null, e);
            }
        }

        /// <summary>
        /// Pop a 16-bit value from the top of the stack.
        /// </summary>
        /// <returns>The 16-bit value popped off the top of the stack.</returns>
        protected ushort PopWord()
        {
            uint stack = CPU.ReadAddressRegister(7);
            CPU.WriteAddressRegister(7, stack + 2);

            ushort value = Memory.ReadWord(stack);
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
        public virtual TrapException?  ExecuteUntilException()
        {
            TrapException? exception;
            FillPrefetch();
            while (!IsEndOfData && !IsEndOfExecution && !ExecutionStopped)
            {
                exception = ExecuteInstruction();
                if (exception != null) return exception;
            }
            return null;
        }

        /// <summary>
        /// Execute until end of data, end of execution, or execution stopped.
        /// </summary>
        public virtual void Execute()
        {
            FillPrefetch();
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
        public virtual TrapException? ExecuteOpCode()
        {
            CurrentInstructionAddress = CPU.CurrentPC;
            var instruction = Decoder.FetchInstruction();
            if (instruction == null)
            {
                return Helpers.CreateTRAPException(TrapVector.IllegalInstruction);
            }
            return ExecutionHandler.Execute(instruction);
        }

        public bool EnableTracing { get; set; } = true;
        public TrapException? Exception { get; protected set; } = null;

        /// <summary>
        /// Execute an instruction and handle any traps/exceptions that arise.
        /// </summary>
        /// <returns>Exception raised in this cycle or null.</returns>
        public virtual TrapException? ExecuteInstruction()
        {
            // Exception that propagates to the next cycle.
            TrapException? exception = Exception;
            TrapException? group0Exception = null;
            TrapException? group12Exception = null;

            InstructionCount++;

            if (exception != null && exception.TrapDetails.Vector == TrapVector.Trace)
            {
                // Handle trace exception here before execution of the next instruction.
                // Pushes PC and SR and sets the PC to the Trace trap vector with SR in
                // Supervisor mode and trace flag off.
                HandleTrapException(exception);
                exception = null;
            }

            SRFlags oldSR = CPU.SR;
            bool traceMode = (oldSR & SRFlags.TraceMode) != 0 && EnableTracing;
            if (!traceMode)
            {
                exception = HandleInterrupt();
            }

            // Execute the instruction.  If an exception is returned, it
            // may be an illegal instruction, Group 0 exception, or
            // anything but an interrupt.
            //
            // Most expected exceptions (such as LINEA exceptions) are returned
            // here rather than thrown to minimize overhead.
            TrapException? e = ExecuteOpCode();
            if (e != null)
            {
                exception = e;
            }

            if (exception != null)
            {
                // Handle Group0 and Group1/2 exceptions here.
                // ExecutingAtAddress updated as part of exception handling.
                try
                {
                    if (exception?.TrapDetails.Group == 0)
                    {
                        group0Exception = exception;
                        HandleGroup0Exception(group0Exception);
                        exception = null;
                    }
                    if (exception != null && !exception.TrapDetails.IsInterrupt)
                    {
                        // Handle Group 1 and Group 2 exceptions before executing the next instruction.
                        // Any Group 0 exception should have already been processed.
                        group12Exception = HandleTrapException(exception);
                        exception = null;
                    }
                }
                catch (TrapException eh)
                {
                    if (exception != null &&
                        (exception.TrapDetails.Vector == TrapVector.BusError ||
                         exception.TrapDetails.Vector == TrapVector.AddressError)
                        &&
                        (eh.TrapDetails.Vector == TrapVector.BusError ||
                         eh.TrapDetails.Vector == TrapVector.AddressError))
                    {
                        // Halt and return for double bus/address faults
                        ExecutionStopped = true;
                        Exception = eh;
                        return eh;
                    }
                    exception = eh;
                }
            }
            else
            {
                // Address of next instruction to be executed.
                CurrentInstructionAddress = CPU.CurrentPC;
            }
            if (traceMode)
            {
                TrapException? traceException = new((ushort)TrapVector.Trace);

                switch ((exception != null, group12Exception != null, group0Exception != null, CPU.SupervisorMode))
                {
                    case (false, false, true, false):
                    case (false, false, true, true):
                    case (false, true, false, true):
                    case (false, true, false, false):
                    case (false, true, true, true):
                    case (false, true, true, false):
                        exception = traceException;
                        CPU.TraceMode = false;
                        break;
                    case (true, true, true, true):
                    case (true, true, false, false):
                    case (true, true, false, true):
                    case (true, false, false, true):
                    case (true, false, false, false):
                    case (true, false, true, false):
                    case (true, false, true, true):
                        CPU.TraceMode = true;
                        break;
                    case (false, false, false, true):
                        exception = traceException;
                        break;
                    case (false, false, false, false):
                        exception = traceException;
                        break;
                    case (true, true, true, false):
                        exception = traceException;
                        break;
                }
            }

            Exception = exception;
            return Exception ?? group12Exception ?? group0Exception;
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
        public virtual byte IPL { get; set; }

        /// <summary>
        /// Current Function Code (FC) outputs.
        /// </summary>
        public virtual FC Fc { get; protected set; } = FC.SupervisorProgram;

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
        public virtual TrapException? HandleInterrupt()
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
                return null;
            }

            // Interrupt is pending, clear the flag for next time.
            InterruptPending = false;

            byte interruptMask = (byte)(((ushort)CPU.SR & 0x0700) >> 8);
            byte vector = (byte)((int)TrapVector.Interrupt + IPL - 1);
            bool handled = IPL == 7 || IPL > interruptMask;
            if (handled)
            {
                TrapException interruptException = new(vector);
                HandleTrapException(interruptException);

                // Acknowledge interrupt to devices.
                SetFCOutputs(interruptException.TrapDetails.Fc);
                Memory.ReadByte(0x000f0000);
                return interruptException;
            }
            return null;
        }

        /// <summary>
        /// Initialize CPU (PC, Prefetch, SR) and ExceptionDetails based on trap vector and exception type.
        /// </summary>
        /// <param name="te"></param>
        /// <param name="evEntry"></param>
        /// <returns>False if trap details are not defined (should only be the case if trap number >= 1024)</returns>
        protected bool SetupException(TrapException te, out ExceptionDetails evEntry)
        {
            if (!te.TrapDetails.IsDefined)
            {
                Logger.Log(LogLevel.Critical, "CPU", () => $"Unknown Trap Exception vector: {te.Vector}");
                evEntry = te.TrapDetails;
                return false;
            }
            evEntry = te.TrapDetails;
            uint trapVectorPC = Memory.ReadLong((uint)te.Vector * 4);

            CPU.PC = trapVectorPC;
            CPU.Prefetch.Clear();
            FillPrefetch();
            CurrentInstructionAddress = CPU.CurrentPC;

            CPU.SR |= SRFlags.SupervisorMode;
            CPU.SR &= ~SRFlags.TraceMode;
            if (evEntry.IsInterrupt)
            {
                // Set priority = interrupt priority
                CPU.SR &= ~SRFlags.InterruptLevel;
                ushort level = (ushort)(te.Vector - (byte)TrapVector.Interrupt + 1);
                CPU.SR |= (SRFlags)(level << 8);
            }

            return true;
        }

        /// <summary>
        /// Handle a Group 0 trap exception.  This is done within 2 (8 MHz) clock cycles and interrupts the
        /// current instruction processing.
        /// </summary>
        /// <param name="te"></param>
        protected void HandleGroup0Exception(TrapException te)
        {
            uint nextInstructionPC = CPU.CurrentPC;
            uint oldPC = CurrentInstructionAddress;
            SRFlags oldSR = CPU.SR;

            if (!SetupException(te, out ExceptionDetails evEntry))
            {
                return;
            }

            EG group = te.TrapDetails.Group;
            if (group != EG.Group0)
            {
                throw new ArgumentException($"Trap exception is not Group 0, it is Group {(byte)group}", nameof(te));
            }

            Logger.Log(LogLevel.Debug, "CPU", () => $"Group 0 Trap Exception handled: {te.Vector} Trap handler: {CPU.CurrentPC:x8} Called from: {oldPC:x8}");

            ushort fcWord = (ushort)evEntry.Fc;
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
            SetFCOutputs(evEntry.Fc);
        }

        /// <summary>
        /// Handle a Group 1 or 2 trap exception.
        /// </summary>
        /// <param name="te"></param>
        protected TrapException? HandleTrapException(TrapException te)
        {
            uint nextInstructionPC = CPU.CurrentPC;
            uint oldPC = CurrentInstructionAddress;
            SRFlags oldSR = CPU.SR;

            if (!SetupException(te, out ExceptionDetails evEntry))
            {
                return null;
            }
            EG group = te.TrapDetails.Group;
            if (group != EG.Group1 && group != EG.Group2)
            {
                throw new ArgumentException("Trap exception is not Group 1 or Group 2", nameof(te));
            }

            uint trapVectorContents = Memory.ReadLong((uint)te.Vector * 4);
            CPU.PC = trapVectorContents;
            CPU.Prefetch.Clear();
            FillPrefetch();

            CPU.SR |= SRFlags.SupervisorMode;
            CPU.SR &= ~SRFlags.TraceMode;
            if (evEntry.IsInterrupt)
            {
                // Set priority = interrupt priority
                CPU.SR &= ~SRFlags.InterruptLevel;
                ushort level = (ushort)(te.Vector - (byte)TrapVector.Interrupt + 1);
                CPU.SR |= (SRFlags)(level << 8);
            }

            // Get name of trap if applicable
            if (te.Vector == (ushort)TrapVector.LineAInstruction)
            {
                string? trapName = Disasm?.GetTrapName(CurrentInstruction.Opcode);
                if (trapName != null)
                {
                    Logger.Log(LogLevel.Debug, "CPU", () => $"{trapName}, handler: {trapVectorContents:x8} Called from: {oldPC:x8}");
                }
                else
                {
                    Logger.Log(LogLevel.Debug, "CPU", () => $"LineA Trap: {CurrentInstruction.Opcode:x4}, handler: {trapVectorContents:x8} Called from: {oldPC:x8}");
                }
            }
            else
            {
                Logger.Log(LogLevel.Debug, "CPU", () => $"Trap: {te.Vector} ({te.TrapDetails.Description}), handler: {trapVectorContents:x8} Called from: {oldPC:x8}");
            }

            switch (evEntry.Group)
            {
                case EG.Group1:
                    PushLong(nextInstructionPC);
                    PushWord((ushort)oldSR);
                    break;
                case EG.Group2:
                    PushLong(oldPC);
                    PushWord((ushort)oldSR);
                    break;
                default:
                    throw new ArgumentException("SHOULD NOT HAPPEN!");
            }
            SetFCOutputs(evEntry.Fc);

            if (trapVectorContents == 0)
            {
                Logger.Log(LogLevel.Critical, "CPU", () => $"Trap Exception  -> address = 0: {te}");
            }

            return te;
        }
    }
}
