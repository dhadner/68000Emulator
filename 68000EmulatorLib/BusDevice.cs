using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Enumeration of bus result status.
    /// </summary>
    public enum BusStatus : byte
    {
        Success = 0x00,         // Normal operation
        WaitState = 0x01,       // Normal operation, but must try again until BusStatus.Success is returned.
        AddressError = 0x02,    // Address error occurred.
        BusError = 0x03,        // External bus error occurred.
    }

    /// <summary>
    /// Represents the result of a bus operation, including the value and status.
    /// </summary>
    /// <typeparam name="T">The data type: must be byte, ushort, or uint.</typeparam>
    public readonly record struct BusResult<T> where T : struct
    {
        /// <summary>
        /// The value read or written.
        /// </summary>
        public T Value { get; init; }

        /// <summary>
        /// The status of the bus operation.
        /// </summary>
        public BusStatus Status { get; init; }

        public BusResult(T value, BusStatus status)
        {
            Value = value;
            Status = status;
        }

        /// <summary>
        /// Static constructor to enforce allowed types at first use of each closed generic type.
        /// </summary>
        static BusResult()
        {
            if (typeof(T) != typeof(byte) && typeof(T) != typeof(ushort) && typeof(T) != typeof(uint))
            {
                string message = $"BusResult<{typeof(T).Name}> is not supported. T must be byte, ushort, or uint.";
                Debug.Assert(false, message);
                throw new NotImplementedException(message);
            }
        }
    }

    /// <summary>
    /// Represents a device connected to the bus.
    /// </summary>
    [TypeConverter(typeof(ExpandableObjectConverter))]
    [RequiresMachineThread]
    public abstract class BusDevice
    {
        /// <summary>
        /// Set by disassembler to allow thread and access checks to be overridden.
        /// </summary>
        public virtual bool Disassembling { get; set; }

        /// <summary>
        /// In debug mode, asserts that the current thread is the machine thread
        /// unless <see cref="Disassembling"/> is in progress.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [RequiresMachineThread]
        public void AssertIsMachineThread()
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
        /// Initialize this bus device.
        /// </summary>
        [RequiresMachineThread]
        public virtual Result<string> Initialize()
        {
            Reset(true);
            return Ok();
        }

        /// <summary>
        /// Reset this bus device.
        /// </summary>
        /// <param name="initialize">Indicates whether the reset should also perform initialization</param>
        [RequiresMachineThread]
        public virtual void Reset(bool initialize = false)
        {
        }

        /// <summary>
        /// Read a byte and return the value and bus status.
        /// </summary>
        /// <param name="address">Address</param>
        /// <returns>Value</returns>
        [RequiresMachineThread]
        public abstract BusResult<byte> ReadByte(uint address);

        /// <summary>
        /// Read a 16-bit value (unsigned) and return the value and bus status.
        /// </summary>
        /// <param name="address">Address</param>
        /// <returns>Value</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [RequiresMachineThread]
        public virtual BusResult<ushort> ReadWord(uint address)
        {
            uint evenAddress = address & 0xFFFFFFFE;
            var resultHi = ReadByte(evenAddress);
            if (resultHi.Status != BusStatus.Success)
            {
                return new((ushort)resultHi.Value, resultHi.Status);
            }
            var resultLo = ReadByte(evenAddress + 1);
            if (resultLo.Status != BusStatus.Success)
            {
                return new((ushort)0, resultLo.Status);
            }
            ushort value = (ushort)((resultHi.Value << 8) | resultLo.Value);
            if (evenAddress != address)
            {
                // Unaligned access: happens after the first word has been read.
                return new(value, BusStatus.AddressError);
            }
            return new(value, BusStatus.Success);
        }

        /// <summary>
        /// Read a 32-bit value (unsigned) and return the value and bus status.
        /// </summary>
        /// <param name="address">Address</param>
        /// <returns>Value</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [RequiresMachineThread]
        public virtual BusResult<uint> ReadLong(uint address)
        {
            var resultHi = ReadWord(address);
            if (resultHi.Status != BusStatus.Success)
            {
                return new((uint)resultHi.Value, resultHi.Status);
            }
            var value = (uint)(resultHi.Value << 16);
            var resultLo = ReadWord(address + 2);
            if (resultLo.Status != BusStatus.Success)
            {
                return new(value, resultLo.Status);
            }
            value |= resultLo.Value;
            return new(value, BusStatus.Success);
        }

        /// <summary>
        /// Write a byte and return the bus status.
        /// </summary>
        /// <param name="address">Address</param>
        /// <param name="value">Value to write</param>
        [RequiresMachineThread]
        public abstract BusResult<byte> WriteByte(uint address, byte value);

        /// <summary>
        /// Write a 16-bit value (unsigned) and return the bus status.
        /// </summary>
        /// <param name="address">Address</param>
        /// <param name="value">Value to write</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [RequiresMachineThread]
        public virtual BusResult<ushort> WriteWord(uint address, ushort value)
        {
            uint evenAddress = address & 0xFFFFFFFE;
            var result = WriteByte(evenAddress, (byte)(value >> 8));
            if (result.Status != BusStatus.Success)
            {
                return new(value, result.Status);
            }
            result = WriteByte(evenAddress + 1, (byte)(value & 0xFF));
            if (result.Status != BusStatus.Success)
            {
                return new(value, result.Status);
            }
            if (evenAddress != address)
            {
                // Unaligned access: happens after the first byte has been written.
                return new(value, BusStatus.AddressError);
            }
            return new(value, BusStatus.Success);
        }

        /// <summary>
        /// Write a 32-bit value (unsigned) and return the bus status.
        /// </summary>
        /// <param name="address">Address</param>
        /// <param name="value">Value to write</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [RequiresMachineThread]
        public virtual BusResult<uint> WriteLong(uint address, uint value)
        {
            var result = WriteWord(address, (ushort)(value >> 16));
            if (result.Status != BusStatus.Success)
            {
                return new(value, result.Status);
            }
            result = WriteWord(address + 2, (ushort)(value & 0xFFFF));
            if (result.Status != BusStatus.Success)
            {
                return new(value, result.Status);
            }
            return new(value, BusStatus.Success);
        }
    }
}
