using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PendleCodeMonkey.MC68000EmulatorLib;
using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;
using Xunit;

namespace PendleCodeMonkey.MC68000Emulator.Tests
{

    public class SnowSingleStepTests
    {
        public static IEnumerable<object[]> GetInstructionTestFiles()
        {
            var testDataDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @"..\..\..\..\..\snow\testdata\m68000\v1"));
            if (!Directory.Exists(testDataDir))
            {
                // If running in a different environment (like a CI pipeline), the path might be different.
                // This is a fallback to the absolute path from your local machine.
                testDataDir = @"C:\Users\danhe\OneDrive\Documents\GitHub\MacView\snow\testdata\m68000\v1";
            }

            var files = Directory.GetFiles(testDataDir, "*.json");
            return files.Select(f => new object[] { Path.GetFileNameWithoutExtension(f) });
        }

        [Theory]
        [MemberData(nameof(GetInstructionTestFiles))]
        public void RunInstructionTest(string instruction)
        {
            var filePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @$"..\..\..\..\..\snow\testdata\m68000\v1\{instruction}.json"));
            if (!File.Exists(filePath))
            {
                filePath = @$"C:\Users\danhe\OneDrive\Documents\GitHub\MacView\snow\testdata\m68000\v1\{instruction}.json";
            }

            using var fileStream = File.OpenRead(filePath);
            using var reader = new StreamReader(fileStream);
        
            var jsonString = reader.ReadToEnd();
            var testcases = JsonSerializer.Deserialize<List<M68kJsonTestCase>>(jsonString);

            foreach (var testcase in testcases)
            {
                var machine = new Machine();
                var cpu = machine.CPU;

                // Set initial state for registers
                cpu.WriteDataRegister(0, testcase.Initial.D0);
                cpu.WriteDataRegister(1, testcase.Initial.D1);
                cpu.WriteDataRegister(2, testcase.Initial.D2);
                cpu.WriteDataRegister(3, testcase.Initial.D3);
                cpu.WriteDataRegister(4, testcase.Initial.D4);
                cpu.WriteDataRegister(5, testcase.Initial.D5);
                cpu.WriteDataRegister(6, testcase.Initial.D6);
                cpu.WriteDataRegister(7, testcase.Initial.D7);

                cpu.WriteAddressRegister(0, testcase.Initial.A0);
                cpu.WriteAddressRegister(1, testcase.Initial.A1);
                cpu.WriteAddressRegister(2, testcase.Initial.A2);
                cpu.WriteAddressRegister(3, testcase.Initial.A3);
                cpu.WriteAddressRegister(4, testcase.Initial.A4);
                cpu.WriteAddressRegister(5, testcase.Initial.A5);
                cpu.WriteAddressRegister(6, testcase.Initial.A6);

                // Set stack pointers based on supervisor state
                cpu.SR = (SRFlags)testcase.Initial.Sr;
                if (cpu.SupervisorMode)
                {
                    cpu.USP = testcase.Initial.Usp;
                    cpu.SSP = testcase.Initial.Ssp;
                    cpu.WriteAddressRegister(7, testcase.Initial.Ssp);
                }
                else
                {
                    cpu.USP = testcase.Initial.Usp;
                    cpu.SSP = testcase.Initial.Ssp; // Set SSP even in user mode
                    cpu.WriteAddressRegister(7, testcase.Initial.Usp);
                }

                // The PC in the test data points after the first prefetch.
                // The Rust emulator subtracts 4 (2 words). Our C# emulator doesn't do prefetch,
                // and Step() expects PC to point at the instruction opcode.
                var instructionStartAddr = testcase.Initial.Pc - 4;

                // Separate executable code from other RAM data.
                var ramData = testcase.Initial.Ram.ToDictionary(r => (uint)r[0], r => (byte)r[1]);
                var executableCode = new List<byte>();
                var currentAddr = instructionStartAddr;
                while (ramData.TryGetValue(currentAddr, out var opByte))
                {
                    executableCode.Add(opByte);
                    ramData.Remove(currentAddr);
                    currentAddr++;
                }

                // Load the contiguous executable code block.
                if (executableCode.Count > 0)
                {
                    machine.LoadExecutableData(executableCode.ToArray(), instructionStartAddr, false);
                }

                // Write the remaining disparate RAM entries.
                foreach (var ramEntry in ramData)
                {
                    machine.Memory.WriteByte(ramEntry.Key, ramEntry.Value);
                }

                // Set PC to the start of the instruction.
                cpu.PC = instructionStartAddr;

                // Execute one instruction
                machine.ExecuteInstruction();

                // Assert final state
                Assert.Equal(testcase.Final.D0, cpu.ReadDataRegister(0));
                Assert.Equal(testcase.Final.D1, cpu.ReadDataRegister(1));
                Assert.Equal(testcase.Final.D2, cpu.ReadDataRegister(2));
                Assert.Equal(testcase.Final.D3, cpu.ReadDataRegister(3));
                Assert.Equal(testcase.Final.D4, cpu.ReadDataRegister(4));
                Assert.Equal(testcase.Final.D5, cpu.ReadDataRegister(5));
                Assert.Equal(testcase.Final.D6, cpu.ReadDataRegister(6));
                Assert.Equal(testcase.Final.D7, cpu.ReadDataRegister(7));

                Assert.Equal(testcase.Final.A0, cpu.ReadAddressRegister(0));
                Assert.Equal(testcase.Final.A1, cpu.ReadAddressRegister(1));
                Assert.Equal(testcase.Final.A2, cpu.ReadAddressRegister(2));
                Assert.Equal(testcase.Final.A3, cpu.ReadAddressRegister(3));
                Assert.Equal(testcase.Final.A4, cpu.ReadAddressRegister(4));
                Assert.Equal(testcase.Final.A5, cpu.ReadAddressRegister(5));
                Assert.Equal(testcase.Final.A6, cpu.ReadAddressRegister(6));

                // The M68000_SR_MASK from Rust is 0xA71F. We should only compare these bits.
                const ushort srMask = 0xA71F;
                Assert.Equal(testcase.Final.Sr & srMask, (ushort)cpu.SR & srMask);

                // Check stack pointers after execution
                if ((testcase.Final.Sr & 0x2000) != 0) // Is supervisor
                {
                    Assert.Equal(testcase.Final.Ssp, cpu.ReadAddressRegister(7));
                    Assert.Equal(testcase.Final.Usp, cpu.USP);
                }
                else
                {
                    Assert.Equal(testcase.Final.Usp, cpu.ReadAddressRegister(7));
                    Assert.Equal(testcase.Final.Ssp, cpu.SSP);
                }

                foreach (var ramEntry in testcase.Final.Ram)
                {
                    var address = (uint)ramEntry[0];
                    var expectedValue = (byte)ramEntry[1];
                    
                    var finalCpu = new CPU { SR = (SRFlags)testcase.Final.Sr };
                    if (finalCpu.SupervisorMode && (address >= testcase.Final.Ssp && address < testcase.Final.Ssp + 14))
                    {
                        continue;
                    }
                    Assert.Equal(expectedValue, machine.Memory.ReadByte(address));
                }
            }
        }
    }
}
