using PendleCodeMonkey.MC68000EmulatorLib;
using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;
using System.Collections;
using System.Collections.Generic;
using Xunit;

namespace PendleCodeMonkey.MC68000Emulator.Tests
{
    public class CPUTests
    {
        [Fact]
        public void NewCPU_ShouldNotBeNull()
        {
            CPU cpu = new CPU();

            Assert.NotNull(cpu);
        }

        [Fact]
        public void Reset_ShouldResetState()
        {
            CPU cpu = new()
            {
                DataRegisters = new uint[] { 100, 200, 300, 400, 500, 600, 700, 800 },
                AddressRegisters = new uint[] { 1100, 1200, 1300, 1400, 1500, 1600, 1700 },
                PC = 0x0200,
                USP = 0x4000,
                SSP = 0x4000,
                SR = new SRValue { Carry = true }
            };

            cpu.Reset();

            Assert.Equal((uint)0, cpu.DataRegisters[0]);
            Assert.Equal((uint)0, cpu.DataRegisters[1]);
            Assert.Equal((uint)0, cpu.DataRegisters[2]);
            Assert.Equal((uint)0, cpu.DataRegisters[3]);
            Assert.Equal((uint)0, cpu.DataRegisters[4]);
            Assert.Equal((uint)0, cpu.DataRegisters[5]);
            Assert.Equal((uint)0, cpu.DataRegisters[6]);
            Assert.Equal((uint)0, cpu.DataRegisters[7]);
            Assert.Equal((uint)0, cpu.AddressRegisters[0]);
            Assert.Equal((uint)0, cpu.AddressRegisters[1]);
            Assert.Equal((uint)0, cpu.AddressRegisters[2]);
            Assert.Equal((uint)0, cpu.AddressRegisters[3]);
            Assert.Equal((uint)0, cpu.AddressRegisters[4]);
            Assert.Equal((uint)0, cpu.AddressRegisters[5]);
            Assert.Equal((uint)0, cpu.AddressRegisters[6]);
            Assert.Equal((uint)0, cpu.PC);
            Assert.Equal((uint)0, cpu.USP);
            Assert.Equal((uint)0, cpu.SSP);
            Assert.Equal((SRValue)0, cpu.SR);
        }

        [Fact]
        public void ReadDataRegister()
        {
            CPU cpu = new CPU
            {
                DataRegisters = new uint[] { 100, 200, 300, 400, 500, 600, 700, 800 }
            };

            uint d0 = cpu.ReadDataRegister(0);
            uint d1 = cpu.ReadDataRegister(1);
            uint d2 = cpu.ReadDataRegister(2);
            uint d3 = cpu.ReadDataRegister(3);
            uint d4 = cpu.ReadDataRegister(4);
            uint d5 = cpu.ReadDataRegister(5);
            uint d6 = cpu.ReadDataRegister(6);
            uint d7 = cpu.ReadDataRegister(7);

            Assert.Equal((uint)100, d0);
            Assert.Equal((uint)200, d1);
            Assert.Equal((uint)300, d2);
            Assert.Equal((uint)400, d3);
            Assert.Equal((uint)500, d4);
            Assert.Equal((uint)600, d5);
            Assert.Equal((uint)700, d6);
            Assert.Equal((uint)800, d7);
        }

        [Theory]
        [InlineData(0x000000AA, OpSize.Byte, 0x123456AA)]
        [InlineData(0x0000AA55, OpSize.Word, 0x1234AA55)]
        [InlineData(0x87654321, OpSize.Long, 0x87654321)]
        public void WriteDataRegister(uint value, OpSize size, uint expectedResult)
        {
            CPU cpu = new CPU
            {
                DataRegisters = new uint[] { 0x12345678, 0, 0, 0, 0, 0, 0, 0 }
            };
            cpu.WriteDataRegister(0, value, size);
            uint d0 = cpu.ReadDataRegister(0);

            // Assert
            Assert.Equal(expectedResult, d0);
        }

        [Fact]
        public void ReadAddressRegister()
        {
            CPU cpu = new CPU
            {
                AddressRegisters = new uint[] { 1100, 1200, 1300, 1400, 1500, 1600, 1700 },
                USP = 0x4000,
                SSP = 0x5000
            };

            uint a0 = cpu.ReadAddressRegister(0);
            uint a1 = cpu.ReadAddressRegister(1);
            uint a2 = cpu.ReadAddressRegister(2);
            uint a3 = cpu.ReadAddressRegister(3);
            uint a4 = cpu.ReadAddressRegister(4);
            uint a5 = cpu.ReadAddressRegister(5);
            uint a6 = cpu.ReadAddressRegister(6);
            uint a7 = cpu.ReadAddressRegister(7);           // Read A7 value whilst in user mode.
            cpu.SR = SRValue.SupervisorModeBit;
            uint a7_ssp = cpu.ReadAddressRegister(7);       // Read A7 value whilst in supervisor mode.

            Assert.Equal((uint)1100, a0);
            Assert.Equal((uint)1200, a1);
            Assert.Equal((uint)1300, a2);
            Assert.Equal((uint)1400, a3);
            Assert.Equal((uint)1500, a4);
            Assert.Equal((uint)1600, a5);
            Assert.Equal((uint)1700, a6);
            Assert.Equal((uint)0x4000, a7);
            Assert.Equal((uint)0x5000, a7_ssp);
        }

        [Theory]
        [InlineData(0x000000AA, OpSize.Byte, 0x123456AA)]
        [InlineData(0x0000AA55, OpSize.Word, 0x1234AA55)]
        [InlineData(0x87654321, OpSize.Long, 0x87654321)]
        public void WriteAddressRegister(uint value, OpSize size, uint expectedResult)
        {
            CPU cpu = new CPU
            {
                AddressRegisters = new uint[] { 0, 0x12345678, 0, 0, 0, 0, 0 }
            };
            cpu.WriteAddressRegister(1, value, size);
            uint a1 = cpu.ReadAddressRegister(1);

            // Assert
            Assert.Equal(expectedResult, a1);
        }

        [Theory]
        [InlineData(0x000000AA, OpSize.Byte, 0x123456AA)]
        [InlineData(0x0000AA55, OpSize.Word, 0x1234AA55)]
        [InlineData(0x87654321, OpSize.Long, 0x87654321)]
        public void WriteAddressRegister_A7(uint value, OpSize size, uint expectedResult)
        {
            CPU cpu = new CPU
            {
                USP = 0x12345678
            };
            cpu.WriteAddressRegister(7, value, size);
            uint a7 = cpu.ReadAddressRegister(7);

            // Assert
            Assert.Equal(expectedResult, a7);
        }

        [Theory]
        [InlineData(0x00123456, OpSize.Byte, 0, 0x00123457)]
        [InlineData(0x00123456, OpSize.Byte, 7, 0x00123458)]
        [InlineData(0x12341234, OpSize.Word, 1, 0x12341236)]
        [InlineData(0x00222222, OpSize.Long, 2, 0x00222226)]
        public void IncrementAddressRegister(uint value, OpSize size, byte reg, uint expectedResult)
        {
            CPU cpu = new();
            for (int i = 0; i < 7; i++)
            {
                cpu.WriteAddressRegister(i, 0);
            }
            cpu.WriteAddressRegister(reg, value);
            uint a = cpu.IncrementAddressRegister(reg, size);

            // Assert
            Assert.Equal(expectedResult, a);
        }

        [Theory]
        [InlineData(0x00123456, OpSize.Byte, 0, 0x00123455)]
        [InlineData(0x00123456, OpSize.Byte, 7, 0x00123454)]
        [InlineData(0x12341234, OpSize.Word, 1, 0x12341232)]
        [InlineData(0x00222222, OpSize.Long, 2, 0x0022221E)]
        public void DecrementAddressRegister(uint value, OpSize size, byte reg, uint expectedResult)
        {
            CPU cpu = new();
            for (int i = 0; i < 7; i++)
            {
                cpu.WriteAddressRegister(i, 0);
            }
            cpu.WriteAddressRegister(reg, value);

            uint a = cpu.DecrementAddressRegister(reg, size);

            // Assert
            Assert.Equal(expectedResult, a);
        }

        public static IEnumerable<object[]> ConditionEvaluationTestData =>
            [
                [(Condition.T, (SRValue)0, true) ],
                [(Condition.F, (SRValue)0, false) ],
                [(Condition.HI, (SRValue)0, true) ],
                [(Condition.HI, SRValue.ZeroBit, false) ],
                [(Condition.HI, SRValue.CarryBit, false) ],
                [(Condition.HI, SRValue.CarryBit | SRValue.ZeroBit, false) ],
                [(Condition.LS, (SRValue)0, false) ],
                [(Condition.LS, SRValue.ZeroBit, true) ],
                [(Condition.LS, SRValue.CarryBit, true) ],
                [(Condition.LS, SRValue.CarryBit | SRValue.ZeroBit, true) ],
                [(Condition.CC, (SRValue)0, true) ],
                [(Condition.CC, SRValue.CarryBit, false) ],
                [(Condition.CS, (SRValue)0, false) ],
                [(Condition.CS, SRValue.CarryBit, true) ],
                [(Condition.NE, (SRValue)0, true) ],
                [(Condition.NE, SRValue.ZeroBit, false) ],
                [(Condition.EQ, (SRValue)0, false) ],
                [(Condition.EQ, SRValue.ZeroBit, true) ],
                [(Condition.VC, (SRValue)0, true) ],
                [(Condition.VC, SRValue.OverflowBit, false) ],
                [(Condition.VS, (SRValue)0, false) ],
                [(Condition.VS, SRValue.OverflowBit, true) ],
                [(Condition.PL, (SRValue)0, true) ],
                [(Condition.PL, SRValue.NegativeBit, false) ],
                [(Condition.MI, (SRValue)0, false) ],
                [(Condition.MI, SRValue.NegativeBit, true) ],
                [(Condition.GE, (SRValue)0, true) ],
                [(Condition.GE, SRValue.NegativeBit, false) ],
                [(Condition.GE, SRValue.OverflowBit, false) ],
                [(Condition.GE, SRValue.NegativeBit | SRValue.OverflowBit, true) ],
                [(Condition.LT, (SRValue)0, false) ],
                [(Condition.LT, SRValue.NegativeBit, true) ],
                [(Condition.LT, SRValue.OverflowBit, true) ],
                [(Condition.LT, SRValue.NegativeBit | SRValue.OverflowBit, false) ],
                [(Condition.LE, SRValue.ZeroBit | SRValue.NegativeBit | SRValue.OverflowBit, true) ]
                ];

        [Theory]
        [MemberData(nameof(ConditionEvaluationTestData))]
        public void EvaluateCondition((Condition condition, SRValue flags, bool expectedResult) data)
        {
            // Arrange
            CPU cpu = new()
            {
                SR = new SRValue((ushort)data.flags)
            };

            // Act
            bool result = cpu.EvaluateCondition(data.condition);

            // Assert
            Assert.Equal(data.expectedResult, result);
        }


    }
}
