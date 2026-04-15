using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;
using System.Net.Http.Headers;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Implementation of the <see cref="CPUState"/> class.
    /// </summary>
    /// <remarks>
    /// This class holds information that can be used to get or set individual elements
    /// of the CPU state. The number of CPU state elements is quite large and therefore it
    /// is preferable to have a single object for handling this state info rather than having
    /// a method (a constructor, for example) with a dozen or more arguments.
    /// </remarks>
    public class CPUState
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CPUState"/> class.
        /// </summary>
        public CPUState()
        {
        }

        /// <summary>
        /// Gets or sets the value of the D0 register.
        /// </summary>
        public Option<uint> D0 { get; set; }

        /// <summary>
        /// Gets or sets the value of the D1 register.
        /// </summary>
        public Option<uint> D1 { get; set; }

        /// <summary>
        /// Gets or sets the value of the D2 register.
        /// </summary>
        public Option<uint> D2 { get; set; }

        /// <summary>
        /// Gets or sets the value of the D3 register.
        /// </summary>
        public Option<uint> D3 { get; set; }

        /// <summary>
        /// Gets or sets the value of the D4 register.
        /// </summary>
        public Option<uint> D4 { get; set; }

        /// <summary>
        /// Gets or sets the value of the D5 register.
        /// </summary>
        public Option<uint> D5 { get; set; }

        /// <summary>
        /// Gets or sets the value of the D6 register.
        /// </summary>
        public Option<uint> D6 { get; set; }

        /// <summary>
        /// Gets or sets the value of the D7 register.
        /// </summary>
        public Option<uint> D7 { get; set; }

        /// <summary>
        /// Gets or sets the value of the A0 register.
        /// </summary>
        public Option<uint> A0 { get; set; }

        /// <summary>
        /// Gets or sets the value of the A1 register.
        /// </summary>
        public Option<uint> A1 { get; set; }

        /// <summary>
        /// Gets or sets the value of the A2 register.
        /// </summary>
        public Option<uint> A2 { get; set; }

        /// <summary>
        /// Gets or sets the value of the A3 register.
        /// </summary>
        public Option<uint> A3 { get; set; }

        /// <summary>
        /// Gets or sets the value of the A4 register.
        /// </summary>
        public Option<uint> A4 { get; set; }

        /// <summary>
        /// Gets or sets the value of the A5 register.
        /// </summary>
        public Option<uint> A5 { get; set; }

        /// <summary>
        /// Gets or sets the value of the A6 register.
        /// </summary>
        public Option<uint> A6 { get; set; }

        /// <summary>
        /// Gets or sets the value of the A7 register.
        /// </summary>
        public Option<uint> A7 { get; set; }

        /// <summary>
        /// Gets or sets the value of the USP register.
        /// </summary>
        public Option<uint> USP { get; set; }

        /// <summary>
        /// Gets or sets the value of the SSP register.
        /// </summary>
        public Option<uint> SSP { get; set; }

        /// <summary>
        /// Gets or sets the value of the Status Register.
        /// </summary>
        public Option<SRValue> SR { get; set; }

        /// <summary>
        /// Gets or sets the value of the Program Counter.
        /// </summary>
        public Option<uint> PC { get; set; }

        /// <summary>
        /// Current PC taking into account the prefetch queue.
        /// That is, the PC adjusted backwards by the size of the prefetch queue
        /// to give the address of the next instruction to be executed or next
        /// extension word to be read.
        /// </summary>
        public Option<uint> CurrentPC
        {
            get
            {
                if (PC.IsSome)
                {
                    if (Prefetch.IsSome)
                    {
                        return PC.Value - Prefetch.Value.Size;
                    }
                }
                return PC;
            }
        }

        /// <summary>
        /// Prefetch queue state.
        /// </summary>
        public Option<PrefetchQueue> Prefetch { get; set; }

        /// <summary>
        /// Transfer values from this <see cref="CPUState"/> instance into the settings in
        /// specified <see cref="CPU"/> instance.
        /// </summary>
        /// <remarks>
        /// Only non-null values in this <see cref="CPUState"/> instance are transferred to the <see cref="CPU"/>.
        /// </remarks>
        /// <param name="cpu">The <see cref="CPU"/> instance into which the state values should be transferred.</param>
        public void ToCPU(CPU cpu)
        {
            if (D0.IsSome)
            {
                cpu.WriteDataRegister(0, D0.Value, None);
            }
            if (D1.IsSome)
            {
                cpu.WriteDataRegister(1, D1.Value, None);
            }
            if (D2.IsSome)
            {
                cpu.WriteDataRegister(2, D2.Value, None);
            }
            if (D3.IsSome)
            {
                cpu.WriteDataRegister(3, D3.Value, None);
            }
            if (D4.IsSome)
            {
                cpu.WriteDataRegister(4, D4.Value, None);
            }
            if (D5.IsSome)
            {
                cpu.WriteDataRegister(5, D5.Value, None);
            }
            if (D6.IsSome)
            {
                cpu.WriteDataRegister(6, D6.Value, None);
            }
            if (D7.IsSome)
            {
                cpu.WriteDataRegister(7, D7.Value, None);
            }

            if (A0.IsSome)
            {
                cpu.WriteAddressRegister(0, A0.Value);
            }
            if (A1.IsSome)
            {
                cpu.WriteAddressRegister(1, A1.Value);
            }
            if (A2.IsSome)
            {
                cpu.WriteAddressRegister(2, A2.Value);
            }
            if (A3.IsSome)
            {
                cpu.WriteAddressRegister(3, A3.Value);
            }
            if (A4.IsSome)
            {
                cpu.WriteAddressRegister(4, A4.Value);
            }
            if (A5.IsSome)
            {
                cpu.WriteAddressRegister(5, A5.Value);
            }
            if (A6.IsSome)
            {
                cpu.WriteAddressRegister(6, A6.Value);
            }
            if (A7.IsSome)
            {
                cpu.WriteAddressRegister(7, A7.Value);
            }

            if (USP.IsSome)
            {
                cpu.USP = USP.Value;
            }
            if (SSP.IsSome)
            {
                cpu.SSP = SSP.Value;
            }

            if (SR.IsSome)
            {
                cpu.SR = SR.Value;
            }

            if (PC.IsSome)
            {
                cpu.PC = PC.Value;
            }

            if (Prefetch.IsSome)
            {
                cpu.Prefetch.From(Prefetch.Value);
            }
        }

        /// <summary>
        /// Transfer values from the specified <see cref="CPU"/> instance into this <see cref="CPUState"/> instance.
        /// </summary>
        /// <param name="cpu">The <see cref="CPU"/> instance from which the state values should be transferred.</param>
        public void From(CPU cpu)
        {
            D0 = cpu.ReadDataRegister(0);
            D1 = cpu.ReadDataRegister(1);
            D2 = cpu.ReadDataRegister(2);
            D3 = cpu.ReadDataRegister(3);
            D4 = cpu.ReadDataRegister(4);
            D5 = cpu.ReadDataRegister(5);
            D6 = cpu.ReadDataRegister(6);
            D7 = cpu.ReadDataRegister(7);

            A0 = cpu.ReadAddressRegister(0);
            A1 = cpu.ReadAddressRegister(1);
            A2 = cpu.ReadAddressRegister(2);
            A3 = cpu.ReadAddressRegister(3);
            A4 = cpu.ReadAddressRegister(4);
            A5 = cpu.ReadAddressRegister(5);
            A6 = cpu.ReadAddressRegister(6);
            A7 = cpu.ReadAddressRegister(7);

            USP = cpu.USP;
            SSP = cpu.SSP;

            SR = cpu.SR;

            PC = cpu.PC;

            Prefetch = cpu.Prefetch.Clone();
        }
    }
}
