using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;
using System.Runtime.CompilerServices;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Data bus that contains only RAM of the size specified.
    /// </summary>
    public class RamBus : DataBus
    {
        /// <summary>
        /// Create a new instance with a single memory device.
        /// </summary>
        /// <param name="memSize"></param>
        public RamBus(uint memSize) : base()
        {
            _memory = new Memory(memSize);
        }

        /// <summary>
        /// List of bus devices
        /// </summary>
        private Memory _memory;

        /// <summary>
        /// Given the address, return the device at this address.
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        [RequiresMachineThread]
        public override BusDevice GetDevice(uint address)
        {
            // Unless class is overridden, only a single device is supported.
            // Mapping of addresses to devices is the responsibility of the derived class.
            return _memory;
        }

        /// <summary>
        /// Reset this bus device.
        /// </summary>
        [RequiresMachineThread]
        public override Result<string> Reset(bool initialize = true)
        {
            return _memory.Reset(initialize);
        }

        /// <summary>
        /// Read a byte and return the value and bus status.
        /// Override this for your device's byte read operation.
        /// </summary>
        /// <param name="address">Address</param>
        /// <returns>Value</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [RequiresMachineThread]
        public override BusResult<byte> ReadByte(uint address)
        {
            if (InWaitState(address))
            {
                return new((byte)0, BusStatus.WaitState);
            }
            var result = _memory.ReadByte(address);
            if (result.Status == BusStatus.AddressError)
            {
                Helpers.RaiseTRAPException(TrapVector.AddressError);
            }
            return result;
        }

        /// <summary>
        /// Write a byte and return the bus status.
        /// Override this for your device's byte write operation.
        /// </summary>
        /// <param name="address">Address</param>
        /// <param name="value">Value to write</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [RequiresMachineThread]
        public override BusResult<byte> WriteByte(uint address, byte value)
        {
            if (InWaitState(address))
            {
                return new(value, BusStatus.WaitState);
            }
            var result = _memory.WriteByte(address, value);
            if (result.Status == BusStatus.AddressError)
            {
                Helpers.RaiseTRAPException(TrapVector.AddressError);
            }
            return result;
        }
    }
}
