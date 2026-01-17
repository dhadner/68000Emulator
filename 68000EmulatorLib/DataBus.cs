using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Represents the system data bus, providing access to connected bus devices and managing address-based device
    /// resolution within a machine context.
    /// </summary>
    /// <remarks>
    /// The DataBus coordinates communication between the machine and its attached bus devices. Most
    /// members require execution on the machine thread, as indicated by the RequiresMachineThread attribute. The
    /// Disassembling property can be set to temporarily relax thread and address access checks, typically for debugging
    /// or disassembly scenarios.
    /// </remarks>
    public abstract class DataBus
    {
        /// <summary>
        /// Creates a new instance of the DataBus class associated with the specified machine.
        /// </summary>
        /// <param name="machine"></param>
        public DataBus()
        {
        }

        /// <summary>
        /// Given the address, return the device at this address.
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        [RequiresMachineThread]
        public abstract BusDevice GetDevice(uint address);

        /// <summary>
        /// Set by disassembler or debugger to allow thread and odd address access checks to be overridden.
        /// </summary>
        public virtual bool PassiveAccess { get; set; }

        /// <summary>
        /// Reset this data bus and all connected devices.
        /// Derived classes must implement this to reset all connected devices.
        /// </summary>
        /// <param name="initialize"></param>
        public abstract Result<string> Reset(bool initialize = true);

        /// <summary>
        /// Reset external devices when CPU RESET instruction is executed.
        /// </summary>
        [RequiresMachineThread]
        public virtual void ResetExternalDevices()
        {
            // Currently does nothing
        }

        /// <summary>
        /// True if the device is in a wait state.
        /// </summary>
        [RequiresMachineThread]
        public virtual bool InWaitState(uint address)
        {
            return false;
        }

        /// <summary>
        /// In debug mode, asserts that the current thread is the machine thread
        /// unless <see cref="PassiveAccess"/> is in progress.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [RequiresMachineThread]
        public void AssertIsMachineThread()
        {
#if DEBUG
            if (PassiveAccess)
            {
                return;
            }
            Machine.AssertIsMachineThread();
#endif
        }

        /// <summary>
        /// Read a byte and return the value and bus status.
        /// Override this for your device's byte read operation.
        /// </summary>
        /// <param name="address">Address</param>
        /// <returns>Value</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [RequiresMachineThread]
        public virtual BusResult<byte> ReadByte(uint address)
        {
            if (InWaitState(address))
            {
                return new((byte)0, BusStatus.WaitState);
            }
            return GetDevice(address).ReadByte(address);
        }

        /// <summary>
        /// Write a byte and return the bus status.
        /// Override this for your device's byte write operation.
        /// </summary>
        /// <param name="address">Address</param>
        /// <param name="value">Value to write</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [RequiresMachineThread]
        public virtual BusResult<byte> WriteByte(uint address, byte value)
        {
            if (InWaitState(address))
            {
                return new(value, BusStatus.WaitState);
            }
            return GetDevice(address).WriteByte(address, value);
        }

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
            if (evenAddress != address && !PassiveAccess)
            {
                // Unaligned access: happens after the first word has been read.
                Helpers.RaiseTRAPException(TrapVector.AddressError);
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
            if (evenAddress != address && !PassiveAccess)
            {
                // Unaligned access: happens after the first byte has been written.
                Helpers.RaiseTRAPException(TrapVector.AddressError); 
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
    /// Open bus device that returns 0xff on reads and accepts all writes.
    /// </summary>
    public class OpenBusDevice : BusDevice
    {
        public OpenBusDevice()
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [RequiresMachineThread]
        public override BusResult<byte> ReadByte(uint address)
        {
            return new(0xff, BusStatus.Success);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [RequiresMachineThread]
        public override BusResult<byte> WriteByte(uint address, byte value)
        {
            return new(value, BusStatus.Success);
        }
    }
}
