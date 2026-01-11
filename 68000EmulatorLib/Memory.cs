using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;
using System.Runtime.CompilerServices;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Implementation of the <see cref="Memory"/> class.
    /// </summary>
    [RequiresMachineThread]
    public class Memory
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Memory"/> class.
        /// </summary>
        /// <param name="memSize">The number of bytes of memory to be allocated.</param>
        public Memory(uint memSize)
        {
            Data = new byte[memSize];
        }

        /// <summary>
        /// Gets or sets the byte array that holds the memory contents.
        /// </summary>
        internal byte[] Data { get; set; }

        /// <summary>
        /// Set by disassembler to allow thread and access checks to be overridden.
        /// </summary>
        public bool Disassembling { get; set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [RequiresMachineThread]
        protected void AssertIsMachineThread()
        {
#if DEBUG
            if (Disassembling)
            {
                return;
            }
            Machine.AssertIsMachineThread();
#endif
        }

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
        public virtual void Clear()
        {
            AssertIsMachineThread();

            Data.AsSpan().Clear();
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
        public virtual byte ReadByte(uint address)
        {
            AssertIsMachineThread();

            address &= CPU.LEGAL_ADDRESS_MASK;
            if (address >= Data.Length)
            {
                Helpers.RaiseTRAPException(TrapVector.AddressError);
            }
            return Data[address];
        }

        /// <summary>
        /// Read the 16-bit value at the specified address.
        /// Throws TrapException on address error (hotpath - used during instruction execution).
        /// </summary>
        /// <param name="address">The address of the memory to be read.</param>
        /// <returns>The 16-bit value that was read from the specified address.</returns>
        /// <exception cref="TrapException">Thrown if an address error occurs while reading memory.</exception>
        [RequiresMachineThread]
        public virtual ushort ReadWord(uint address)
        {
            AssertIsMachineThread();

            address &= CPU.LEGAL_ADDRESS_MASK;
            if ((address & 1) != 0 || address > Data.Length - 2)
            {
                Helpers.RaiseTRAPException(TrapVector.AddressError);
            }
            return (ushort)((Data[address] << 8) + Data[address + 1]);
        }

        /// <summary>
        /// Read the 32-bit value at the specified address.
        /// Throws TrapException on address error (hotpath - used during instruction execution).
        /// </summary>
        /// <param name="address">The address of the memory to be read.</param>
        /// <returns>The 32-bit value that was read from the specified address.</returns>
        /// <exception cref="TrapException">Thrown if an address error occurs while reading memory.</exception>
        [RequiresMachineThread]
        public virtual uint ReadLong(uint address)
        {
            AssertIsMachineThread();

            address &= CPU.LEGAL_ADDRESS_MASK;
            if ((address & 1) != 0 || address > Data.Length - 4)
            {
                Helpers.RaiseTRAPException(TrapVector.AddressError);
            }
            return (uint)((Data[address] << 24) + (Data[address + 1] << 16) + (Data[address + 2] << 8) + Data[address + 3]);
        }

        /// <summary>
        /// Write a byte value to the specified address.
        /// Throws TrapException on address error (hotpath - used during instruction execution).
        /// </summary>
        /// <param name="address">The address at which the value should be written.</param>
        /// <param name="value">The value to be written to the specified address.</param>
        [RequiresMachineThread]
        public virtual void WriteByte(uint address, byte value)
        {
            AssertIsMachineThread();

            address &= CPU.LEGAL_ADDRESS_MASK;
            if (address >= Data.Length)
            {
                Helpers.RaiseTRAPException(TrapVector.AddressError);
            }
            Data[address] = value;
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
        public virtual void WriteWord(uint address, ushort value)
        {
            AssertIsMachineThread();

            address &= CPU.LEGAL_ADDRESS_MASK;
            if ((address & 1) != 0 || address > Data.Length - 2)
            {
                Helpers.RaiseTRAPException(TrapVector.AddressError);
            }
            Data[address] = (byte)((value >> 8) & 0xFF);
            Data[address + 1] = (byte)(value & 0xFF);
        }

        /// <summary>
        /// Write a 32-bit value to the specified address.
        /// Throws TrapException on address error (hotpath - used during instruction execution).
        /// </summary>
        /// <remarks>
        /// Each byte of the 32-bit value is written to memory in sequence from the highest byte to the lowest byte.
        /// </remarks>
        /// <param name="address">The address at which the value should be written.</param>
        /// <param name="value">The value to be written to the specified address.</param>
        /// <exception cref="TrapException">Thrown if an address error occurs while writing memory.</exception>
        [RequiresMachineThread]
        public virtual void WriteLong(uint address, uint value)
        {
            AssertIsMachineThread();

            address &= CPU.LEGAL_ADDRESS_MASK;
            if ((address & 1) != 0 || address > Data.Length - 4)
            {
                Helpers.RaiseTRAPException(TrapVector.AddressError);
            }
            Data[address] = (byte)((value >> 24) & 0xFF);
            Data[address + 1] = (byte)((value >> 16) & 0xFF);
            Data[address + 2] = (byte)((value >> 8) & 0xFF);
            Data[address + 3] = (byte)(value & 0xFF);
        }

        #endregion

        #region Safe Read/Write Methods (return Result for debugger/tooling use)

        /// <summary>
        /// Safely read a byte value at the specified address.
        /// Returns Result instead of throwing (non-hotpath - used by debugger/tooling).
        /// </summary>
        /// <param name="address">The address of the memory to be read.</param>
        /// <returns>Result containing the byte value or an error message.</returns>
        [RequiresMachineThread]
        public virtual Result<byte, string> TryReadByte(uint address)
        {
            AssertIsMachineThread();

            address &= CPU.LEGAL_ADDRESS_MASK;
            if (address >= Data.Length)
            {
                return Result<string>.Err($"Address ${address:X8} out of range (max ${Data.Length - 1:X8})");
            }
            return Ok(Data[address]);
        }

        /// <summary>
        /// Safely read a 16-bit value at the specified address.
        /// Returns Result instead of throwing (non-hotpath - used by debugger/tooling).
        /// </summary>
        /// <param name="address">The address of the memory to be read.</param>
        /// <returns>Result containing the word value or an error message.</returns>
        [RequiresMachineThread]
        public virtual Result<ushort, string> TryReadWord(uint address)
        {
            AssertIsMachineThread();

            address &= CPU.LEGAL_ADDRESS_MASK;
            if ((address & 1) != 0)
            {
                return Result<string>.Err($"Word read at odd address ${address:X8}");
            }
            if (address > Data.Length - 2)
            {
                return Result<string>.Err($"Address ${address:X8} out of range for word read (max ${Data.Length - 2:X8})");
            }
            return Ok((ushort)((Data[address] << 8) + Data[address + 1]));
        }

        /// <summary>
        /// Safely read a 32-bit value at the specified address.
        /// Returns Result instead of throwing (non-hotpath - used by debugger/tooling).
        /// </summary>
        /// <param name="address">The address of the memory to be read.</param>
        /// <returns>Result containing the long value or an error message.</returns>
        [RequiresMachineThread]
        public virtual Result<uint, string> TryReadLong(uint address)
        {            
            AssertIsMachineThread();

            address &= CPU.LEGAL_ADDRESS_MASK;
            if ((address & 1) != 0)
            {
                return Result<string>.Err($"Long read at odd address ${address:X8}");
            }
            if (address > Data.Length - 4)
            {
                return Result<string>.Err($"Address ${address:X8} out of range for long read (max ${Data.Length - 4:X8})");
            }
            return Ok((uint)((Data[address] << 24) + (Data[address + 1] << 16) + (Data[address + 2] << 8) + Data[address + 3]));
        }

        /// <summary>
        /// Safely write a byte value to the specified address.
        /// Returns Result instead of throwing (non-hotpath - used by debugger/tooling).
        /// </summary>
        /// <param name="address">The address at which the value should be written.</param>
        /// <param name="value">The value to be written to the specified address.</param>
        /// <returns>Result indicating success or an error message.</returns>
        [RequiresMachineThread]
        public virtual Result<string> TryWriteByte(uint address, byte value)
        {
            AssertIsMachineThread();

            address &= CPU.LEGAL_ADDRESS_MASK;
            if (address >= Data.Length)
            {
                return Result<string>.Err($"Address ${address:X8} out of range (max ${Data.Length - 1:X8})");
            }
            Data[address] = value;
            return Ok();
        }

        /// <summary>
        /// Safely write a 16-bit value to the specified address.
        /// Returns Result instead of throwing (non-hotpath - used by debugger/tooling).
        /// </summary>
        /// <param name="address">The address at which the value should be written.</param>
        /// <param name="value">The value to be written to the specified address.</param>
        /// <returns>Result indicating success or an error message.</returns>
        [RequiresMachineThread]
        public virtual Result<string> TryWriteWord(uint address, ushort value)
        {
            AssertIsMachineThread();

            address &= CPU.LEGAL_ADDRESS_MASK;
            if ((address & 1) != 0)
            {
                return Result<string>.Err($"Word write at odd address ${address:X8}");
            }
            if (address > Data.Length - 2)
            {
                return Result<string>.Err($"Address ${address:X8} out of range for word write (max ${Data.Length - 2:X8})");
            }
            Data[address] = (byte)((value >> 8) & 0xFF);
            Data[address + 1] = (byte)(value & 0xFF);
            return Ok();
        }

        /// <summary>
        /// Safely write a 32-bit value to the specified address.
        /// Returns Result instead of throwing (non-hotpath - used by debugger/tooling).
        /// </summary>
        /// <param name="address">The address at which the value should be written.</param>
        /// <param name="value">The value to be written to the specified address.</param>
        /// <returns>Result indicating success or an error message.</returns>
        [RequiresMachineThread]
        public virtual Result<string> TryWriteLong(uint address, uint value)
        {
            AssertIsMachineThread();

            address &= CPU.LEGAL_ADDRESS_MASK;
            if ((address & 1) != 0)
            {
                return Result<string>.Err($"Long write at odd address ${address:X8}");
            }
            if (address > Data.Length - 4)
            {
                return Result<string>.Err($"Address ${address:X8} out of range for long write (max ${Data.Length - 4:X8})");
            }
            Data[address] = (byte)((value >> 24) & 0xFF);
            Data[address + 1] = (byte)((value >> 16) & 0xFF);
            Data[address + 2] = (byte)((value >> 8) & 0xFF);
            Data[address + 3] = (byte)(value & 0xFF);
            return Ok();
        }

        /// <summary>
        /// Safely read a block of bytes from memory.
        /// Returns Result instead of throwing (non-hotpath - used by debugger/tooling).
        /// </summary>
        /// <param name="address">Start address.</param>
        /// <param name="length">Number of bytes to read.</param>
        /// <returns>Result containing the byte array or an error message.</returns>
        [RequiresMachineThread]
        public virtual Result<byte[], string> TryReadBytes(uint address, uint length)
        {
            AssertIsMachineThread();

            address &= CPU.LEGAL_ADDRESS_MASK;
            if (address + length > Data.Length)
            {
                return Result<string>.Err($"Read range ${address:X8}-${address + length - 1:X8} exceeds memory bounds (max ${Data.Length - 1:X8})");
            }
            byte[] result = new byte[length];
            Array.Copy(Data, address, result, 0, length);
            return Result<byte[], string>.Ok(result);
        }

        /// <summary>
        /// Safely write a block of bytes to memory.
        /// Returns Result instead of throwing (non-hotpath - used by debugger/tooling).
        /// </summary>
        /// <param name="address">Start address.</param>
        /// <param name="data">Bytes to write.</param>
        /// <returns>Result indicating success or an error message.</returns>
        [RequiresMachineThread]
        public virtual Result<string> TryWriteBytes(uint address, byte[] data)
        {
            AssertIsMachineThread();

            if (data == null)
            {
                return Result<string>.Err("Data array is null");
            }
            address &= CPU.LEGAL_ADDRESS_MASK;
            if (address + data.Length > Data.Length)
            {
                return Result<string>.Err($"Write range ${address:X8}-${address + (uint)data.Length - 1:X8} exceeds memory bounds (max ${Data.Length - 1:X8})");
            }
            Array.Copy(data, 0, Data, address, data.Length);
            return Ok();
        }

        #endregion
    }
}