using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PendleCodeMonkey.MC68000EmulatorLib;
using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;
using Xunit;
using Xunit.Abstractions;

namespace PendleCodeMonkey.MC68000Emulator.Tests
{

    public class SnowSingleStepTests
    {
        private readonly ITestOutputHelper _output;

        public SnowSingleStepTests(ITestOutputHelper output)
        {
            _output = output;
        }

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
            var filePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @$"..\..\..\..\TestData\m68000\v1\{instruction}.json"));

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

                TrapException? trapException = null;
                bool stepException = false;
                try
                {
                    // Execute one instruction
                    trapException = machine.ExecuteInstruction();
                }
                catch (TrapException ex)
                {
                    trapException = ex;
                }
                finally
                {
                    stepException = trapException != null;
                }

                if (stepException)
                {
                    _output.WriteLine($"Instruction: {instruction}, Test Case: {testcase.Name} had exception: {trapException}");
                }

                // Assert final state
                var actualD0 = cpu.ReadDataRegister(0);
                Assert.True(testcase.Final.D0 == actualD0, $"D0 mismatch. Expected: ${testcase.Final.D0:x8} ({testcase.Final.D0}), Actual: ${actualD0:x8} ({actualD0})");
                var actualD1 = cpu.ReadDataRegister(1);
                Assert.True(testcase.Final.D1 == actualD1, $"D1 mismatch. Expected: ${testcase.Final.D1:x8} ({testcase.Final.D1}), Actual: ${actualD1:x8} ({actualD1})");
                var actualD2 = cpu.ReadDataRegister(2);
                Assert.True(testcase.Final.D2 == actualD2, $"D2 mismatch. Expected: ${testcase.Final.D2:x8} ({testcase.Final.D2}), Actual: ${actualD2:x8} ({actualD2})");
                var actualD3 = cpu.ReadDataRegister(3);
                Assert.True(testcase.Final.D3 == actualD3, $"D3 mismatch. Expected: ${testcase.Final.D3:x8} ({testcase.Final.D3}), Actual: ${actualD3:x8} ({actualD3})");
                var actualD4 = cpu.ReadDataRegister(4);
                Assert.True(testcase.Final.D4 == actualD4, $"D4 mismatch. Expected: ${testcase.Final.D4:x8} ({testcase.Final.D4}), Actual: ${actualD4:x8} ({actualD4})");
                var actualD5 = cpu.ReadDataRegister(5);
                Assert.True(testcase.Final.D5 == actualD5, $"D5 mismatch. Expected: ${testcase.Final.D5:x8} ({testcase.Final.D5}), Actual: ${actualD5:x8} ({actualD5})");
                var actualD6 = cpu.ReadDataRegister(6);
                Assert.True(testcase.Final.D6 == actualD6, $"D6 mismatch. Expected: ${testcase.Final.D6:x8} ({testcase.Final.D6}), Actual: ${actualD6:x8} ({actualD6})");
                var actualD7 = cpu.ReadDataRegister(7);
                Assert.True(testcase.Final.D7 == actualD7, $"D7 mismatch. Expected: ${testcase.Final.D7:x8} ({testcase.Final.D7}), Actual: ${actualD7:x8} ({actualD7})");

                var actualA0 = cpu.ReadAddressRegister(0);
                Assert.True(testcase.Final.A0 == actualA0, $"A0 mismatch. Expected: {testcase.Final.A0:x8}, Actual: {actualA0:x8}");
                var actualA1 = cpu.ReadAddressRegister(1);
                Assert.True(testcase.Final.A1 == actualA1, $"A1 mismatch. Expected: {testcase.Final.A1:x8}, Actual: {actualA1:x8}");
                var actualA2 = cpu.ReadAddressRegister(2);
                Assert.True(testcase.Final.A2 == actualA2, $"A2 mismatch. Expected: {testcase.Final.A2:x8}, Actual: {actualA2:x8}");
                var actualA3 = cpu.ReadAddressRegister(3);
                Assert.True(testcase.Final.A3 == actualA3, $"A3 mismatch. Expected: {testcase.Final.A3:x8}, Actual: {actualA3:x8}");
                var actualA4 = cpu.ReadAddressRegister(4);
                Assert.True(testcase.Final.A4 == actualA4, $"A4 mismatch. Expected: {testcase.Final.A4:x8}, Actual: {actualA4:x8}");
                var actualA5 = cpu.ReadAddressRegister(5);
                Assert.True(testcase.Final.A5 == actualA5, $"A5 mismatch. Expected: {testcase.Final.A5:x8}, Actual: {actualA5:x8}");
                var actualA6 = cpu.ReadAddressRegister(6);
                Assert.True(testcase.Final.A6 == actualA6, $"A6 mismatch. Expected: {testcase.Final.A6:x8}, Actual: {actualA6:x8}");

                // The M68000_SR_MASK from Rust is 0xA71F. We should only compare these bits.
                const ushort srMask = 0xA71F;
                var expectedSr = testcase.Final.Sr & srMask;
                var actualSr = (ushort)cpu.SR & srMask;
                Assert.True(expectedSr == actualSr, $"SR mismatch. Expected: ${expectedSr:x4}, Actual: ${actualSr:x4}");

                // Check stack pointers after execution
                if ((testcase.Final.Sr & 0x2000) != 0) // Is supervisor
                {
                    var actualSsp = cpu.ReadAddressRegister(7);
                    Assert.True(testcase.Final.Ssp == actualSsp, $"SSP mismatch. Expected: ${testcase.Final.Ssp:x8}, Actual: ${actualSsp:x8}");
                    var actualUsp = cpu.USP;
                    Assert.True(testcase.Final.Usp == actualUsp, $"USP mismatch. Expected: ${testcase.Final.Usp:x8}, Actual: ${actualUsp:x8}");
                }
                else
                {
                    var actualUsp = cpu.ReadAddressRegister(7);
                    Assert.True(testcase.Final.Usp == actualUsp, $"USP mismatch. Expected: ${testcase.Final.Usp:x8}, Actual: ${actualUsp:x8}");
                    var actualSsp = cpu.SSP;
                    Assert.True(testcase.Final.Ssp == actualSsp, $"SSP mismatch. Expected: ${testcase.Final.Ssp:x8}, Actual: ${actualSsp:x8}");
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
                    var actualValue = machine.Memory.ReadByte(address);
                    Assert.True(expectedValue == actualValue, $"RAM mismatch at $0x{address:x8}. Expected: ${expectedValue:x2} ({expectedValue}), Actual: ${actualValue:x2} ({actualValue})");
                }
            }
        }
    }
}
