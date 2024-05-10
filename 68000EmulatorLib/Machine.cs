using System;
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
        internal const uint _memorySize = 0x01000000;     // Default to 16MB of memory allocated for emulator (the max an actual 68000 processor can address).

        protected uint _loadedAddress;
        protected uint _dataLength;

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
        public Machine(uint? memorySize = null) : this(new Memory(memorySize ?? _memorySize))
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
        /// <remarks>Singleton for this machine to keep
        /// garbage collection to a minimum.
        /// </remarks>
        /// 
        protected Instruction CurrentInstruction { get; set; }

        /// <summary>
        /// Gets a value indicating if the machine has reached the end of the loaded executable data.
        /// </summary>
        protected virtual bool IsEndOfData => CPU.PC >= _loadedAddress + _dataLength;

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
        public IDebugger? Debugger { get; set; }

        /// <summary>
        /// Configuration Option: True if machine should
        /// end execution when RTS call depth reaches 0.
        /// </summary>
        public bool EndWhenCallDepthIsZero { get; set; } = true;

        /// <summary>
        /// Gets a value indicating if the execution of code has been stopped by a STOP instruction.
        /// </summary>
        protected bool ExecutionStopped { get; set; }

        /// <summary>
        /// Reset the machine to its default state.
        /// </summary>
        public virtual void Reset()
        {
            Memory.Clear();
            CPU.Reset();
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
            StringBuilder sb = new StringBuilder();

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
            CPUState state = new CPUState();
            state.TransferStateFromCPU(CPU);
            return state;
        }

        /// <summary>
        /// Transfer the state from the CPU to the <see cref="CPUState"/> object. Used to minimize
        /// new object creation/GC loading.
        /// </summary>
        /// <param name="state">A <see cref="CPUState"/> object that will receive the current CPU state settings.</param>
        public void GetCPUState(ref CPUState state)
        {
            state.TransferStateFromCPU(CPU);
        }

        /// <summary>
        /// Set the state of settings in the CPU according to the values in the supplied <see cref="CPUState"/> object.
        /// </summary>
        /// <remarks>
        /// Only non-null values in the supplied <see cref="CPUState"/> instance will be transferred to the CPU, all other
        /// CPU settings will be unaffected.
        /// </remarks>
        /// <param name="state">A <see cref="CPUState"/> object containing the new CPU state settings.</param>
        public void SetCPUState(CPUState state)
        {
            state.TransferStateToCPU(CPU);
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
        /// <param name="clearBeforeLoad"><c>true</c> to clear all memory before loading</param>
        /// <returns><c>null</c>if memory loaded successfully, error message if load error occurred</returns>
        public string? LoadProgram(string sFile, bool clearBeforeLoad = true)
        {
            if (clearBeforeLoad)
            {
                Memory.Clear();
            }
            SRecordLoader loader = new SRecordLoader(this);
            string? errMsg = loader.Load(sFile, out uint? startingAddress, out uint lowestAddress, out uint highestAddress);
            if (errMsg == null)
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
        private byte[] ToByteArray(ushort[] data)
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
        /// This method keeps executing instructions until the program terminates and is therefore the
        /// main entry point for executing 68000 code.
        /// </remarks>
        /// <exception cref="TrapException"/>
        public virtual void Execute()
        {
            TrapException? exception;
            while (!IsEndOfData && !IsEndOfExecution && !ExecutionStopped)
            {
                exception = ExecuteInstruction();
                if (exception != null) throw exception;
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
        /// routine to the caller for handling
        /// without the overhead of stack frame crawling, etc.</exception>
        public virtual TrapException? ExecuteInstruction()
        {
            var instruction = Decoder.FetchInstruction();
            return ExecutionHandler.Execute(instruction);
        }

        /// <summary>
        /// Stop instruction execution (as a result of executing a STOP instruction).
        /// </summary>
        internal void StopExecution()
        {
            ExecutionStopped = true;
        }
    }
}
