using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Implementation of the <see cref="Memory"/> class.
    /// </summary>
    [RequiresMachineThread]
    public class Memory : BusDevice
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Memory"/> class.
        /// </summary>
        /// <param name="memSize">The number of bytes of memory to be allocated.</param>
        public Memory(uint memSize) : base()
        {
            Data = new byte[memSize];
        }

        /// <summary>
        /// Gets or sets the byte array that holds the memory contents.
        /// </summary>
        public byte[] Data { get; set; }

        /// <summary>
        /// Load data into the specified address, optionally clearing all memory before doing so.
        /// </summary>
        /// <param name="data">The data to be loaded.</param>
        /// <param name="loadAddress">The address at which the data should be loaded.</param>
        /// <param name="clearBeforeLoad"><c>true</c> if all memory should be cleared before loading, otherwise <c>false</c>.</param>
        /// <returns><c>true</c> if the data was loaded into memory, otherwise <c>false</c>.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        [RequiresMachineThread]
        public virtual bool LoadData(byte[] data, uint loadAddress, bool clearBeforeLoad = true)
        {
            AssertIsMachineThread();
            loadAddress &= 0x00FFFFFF;

            // Check that the data being loaded will actually fit at the specified load address.
            if (loadAddress + data.Length > Data.Length)
            {
                return false;
            }

            Span<byte> machineMemorySpan = Data;
            if (clearBeforeLoad)
            {
                machineMemorySpan.Clear();
            }
            Span<byte> dataSpan = data;
            Span<byte> loadMemorySpan = machineMemorySpan.Slice((int)loadAddress, dataSpan.Length);
            dataSpan.CopyTo(loadMemorySpan);
            return true;
        }

        /// <summary>
        /// Clear all of the <see cref="Memory"/> instance's data.
        /// </summary>
        [RequiresMachineThread]
        public override Result<string> Reset(bool initialize = true)
        {
            AssertIsMachineThread();

            Data.AsSpan().Clear();
            return Ok();
        }

        /// <summary>
        /// Return the specified block of memory.
        /// </summary>
        /// <param name="address">Start address of the requested block of memory.</param>
        /// <param name="length">Length (in bytes) of the block of memory to be retrieved.</param>
        /// <returns>Read-only copy of the requested memory.</returns>
        [RequiresMachineThread]
        public virtual ReadOnlySpan<byte> DumpMemory(uint address, uint length)
        {
            AssertIsMachineThread();

            address &= CPU.LEGAL_ADDRESS_MASK;

            if (address + length > Data.Length)
            {
                return null;
            }

            ReadOnlySpan<byte> dumpMem = Data.AsSpan().Slice((int)address, (int)length);
            return dumpMem;
        }

        #region Hotpath Read/Write Methods (throw exceptions for 68000 trap handling)

        /// <summary>
        /// Read the byte value at the specified address.
        /// Throws TrapException on address error (hotpath - used during instruction execution).
        /// </summary>
        /// <param name="address">The address of the memory to be read.</param>
        /// <returns>The value that was read from the specified address.</returns>
        [RequiresMachineThread]
        public override BusResult<byte> ReadByte(uint address)
        {
            AssertIsMachineThread();

            address &= CPU.LEGAL_ADDRESS_MASK;
            if (address >= Data.Length)
            {
                Helpers.RaiseTRAPException(TrapVector.BusError);
            }
            return new(Data[address], BusStatus.Success);
        }

        /// <summary>
        /// Read the 16-bit value at the specified address.
        /// Throws TrapException on address error (hotpath - used during instruction execution).
        /// </summary>
        /// <param name="address">The address of the memory to be read.</param>
        /// <returns>The 16-bit value that was read from the specified address.</returns>
        /// <exception cref="TrapException">Thrown if an address error occurs while reading memory.</exception>
        [RequiresMachineThread]
        public override BusResult<ushort> ReadWord(uint address)
        {
            var result = base.ReadWord(address);
            if (result.Status == BusStatus.AddressError)
            {
                Helpers.RaiseTRAPException(TrapVector.AddressError);
            }
            return result;
        }

        /// <summary>
        /// Write a byte value to the specified address.
        /// Throws TrapException on address error (hotpath - used during instruction execution).
        /// </summary>
        /// <param name="address">The address at which the value should be written.</param>
        /// <param name="value">The value to be written to the specified address.</param>
        [RequiresMachineThread]
        public override BusResult<byte> WriteByte(uint address, byte value)
        {
            AssertIsMachineThread();

            address &= CPU.LEGAL_ADDRESS_MASK;
            if (address >= Data.Length)
            {
                Helpers.RaiseTRAPException(TrapVector.BusError);
            }
            Data[address] = value;
            return new BusResult<byte>(value, BusStatus.Success);
        }

        /// <summary>
        /// Write a 16-bit value to the specified address.
        /// Throws TrapException on address error (hotpath - used during instruction execution).
        /// </summary>
        /// <remarks>
        /// The 16-bit value is written to memory as high byte followed by low byte.
        /// </remarks>
        /// <param name="address">The address at which the value should be written.</param>
        /// <param name="value">The value to be written to the specified address.</param>
        /// <exception cref="TrapException">Thrown if an address error occurs while writing memory.</exception>
        [RequiresMachineThread]
        public override BusResult<ushort> WriteWord(uint address, ushort value)
        {
            uint evenAddress = address & 0xFFFFFFFE;
            var result = base.WriteWord(evenAddress, value);
            if ((address & 1) != 0)
            {
                Helpers.RaiseTRAPException(TrapVector.AddressError);
            }
            return result;
        }

        #endregion
    
    
    }
}