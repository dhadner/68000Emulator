using PendleCodeMonkey.MC68000EmulatorLib.Enumerations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using InstructionDecoder = PendleCodeMonkey.MC68000EmulatorLib.Machine.InstructionDecoder;

namespace PendleCodeMonkey.MC68000Emulator.Tests
{
    public class InstructionDecoderTests
    {
        private const string TEST_DATA_PATH = @"..\..\..\..\68000Emulator.Tests\68000.official.json";
        private readonly ITestOutputHelper _output;

        public struct LegalOpcodeData
        {
            public ushort Opcode;
            public bool Legal;
            public string Description;
        }

        public InstructionDecoderTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public static List<LegalOpcodeData> GetOpcodeLegalList()
        {
            var filePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), TEST_DATA_PATH));
            using var fileStream = File.OpenRead(filePath);
            using var reader = new StreamReader(fileStream);

            var jsonString = reader.ReadToEnd();

            // Deserialize as Dictionary<string, string> since that's the actual JSON format
            var opcodeDict = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonString);
            Assert.NotNull(opcodeDict);

            var data = new List<LegalOpcodeData>();
            foreach (var kvp in opcodeDict)
            {
                data.Add(new LegalOpcodeData
                {
                    Opcode = ushort.Parse(kvp.Key, NumberStyles.HexNumber),
                    Legal = kvp.Value != "None",
                    Description = kvp.Value
                });
            }
            data.Reverse();  // File is ordered by opcode descending, so reverse.
            return data;
        }

        private static string? ToBinary(uint? value, int width = 8)
        {
            string? result = null;
            if (value.HasValue)
            {
                result = $"0b{Convert.ToString(value!.Value, 2).PadLeft(width, '0')}";
            }
            return result;
        }

        [Fact]
        public void ShouldIdentifyIllegalOpcodes()
        {
            const uint START_ADDRESS = 0x4000;
            var list = GetOpcodeLegalList();

            var machine = new Machine();
            InstructionDecoder decoder = new(machine);

            byte[] data = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            machine.LoadExecutableData(data, START_ADDRESS);
            bool passed = true;
            int[] fails = [0, 0];
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var opcodeData in list)
                {
                    machine.Memory.WriteWord(START_ADDRESS, opcodeData.Opcode);
                    Assert.Equal(opcodeData.Opcode, machine.Memory.ReadWord(START_ADDRESS).Value);

                    machine.SetPC(START_ADDRESS);

                    var instruction = decoder.FetchInstruction();
                    if (instruction != null)
                    {
                        // Instruction was decoded.
                        if ((instruction.Opcode & 0xf000) == 0xf000 ||
                            (instruction.Opcode & 0xa000) == 0xa000 ||
                            (instruction.Info.HandlerID == OpHandlerID.ILLEGAL))
                        {
                            // ILLEGAL, F-line and A-line traps are ok
                            Assert.True(true);
                        }
                        else
                        {
                            // Decoded, so it must be legal.
                            if (!opcodeData.Legal)
                            {
                                passed = false;
                                fails[pass] += 1;

                                AddrMode? srcEA = instruction.SourceAddrMode.IsSome ? (AddrMode)instruction.SourceAddrMode.Value : null;
                                ushort? srcExt1 = instruction.SourceExtWord1.ToNullable();
                                ushort? srcExt2 = instruction.SourceExtWord2.ToNullable();
                                string src = $"EA: {srcEA} {ToBinary((byte?)srcEA, 6)} Ext1: {srcExt1} Ext2: {srcExt2}";

                                AddrMode? dstEA = instruction.DestAddrMode.IsSome ? (AddrMode)instruction.DestAddrMode.Value : null;
                                ushort? dstExt1 = instruction.DestExtWord1.ToNullable();
                                ushort? dstExt2 = instruction.DestExtWord2.ToNullable();
                                string dst = $"EA: {dstEA} {ToBinary((byte?)dstEA, 6)} Ext1: {dstExt1} Ext2: {dstExt2}";

                                string size = instruction.Size.IsSome ? instruction.Size.Value switch
                                {
                                    OpSize.Byte => ".B",
                                    OpSize.Word => ".W",
                                    OpSize.Long => ".L",
                                    _ => ""
                                } : "";
                                if (pass == 0)
                                {
                                    _output.WriteLine($"0x{opcodeData.Opcode:x4}{size} Opcode handled but should not be ({instruction.Info.Mnemonic}, src: {src}, dst: {dst})");
                                }
                            }
                        }
                    }
                    else // instruction is null
                    {
                        // Instruction was not decoded, so it MUST be illegal.
                        if (opcodeData.Legal)
                        {
                            passed = false;
                            fails[pass] += 1;

                            if (pass == 0)
                            {
                                _output.WriteLine($"0x{opcodeData.Opcode:x4}: Opcode NOT handled but should be: {opcodeData.Description}");
                            }
                        }
                    }
                }
            }
            if (fails[0] != fails[1])
            {
                _output.WriteLine($"Fails in pass 1: {fails[0]}");
                _output.WriteLine($"Fails in pass 2: {fails[1]}");
            }
            else if (passed)
            {
                _output.WriteLine($"PASSED!");
            }
           Assert.True(passed, $"FAIL: {fails[0]} failures. See error messages below for details.");
        }

        [Fact]
        public void NewDecoder_ShouldNotBeNull()
        {
            InstructionDecoder decoder = new(new Machine());

            Assert.NotNull(decoder);
        }

        [Fact]
        public void ReadNextPCWord_ShouldReturnValueWhenPCIsWithinLoadedData()
        {
            Machine machine = new();
            var _ = machine.LoadExecutableData([1, 2, 3, 4, 5, 6], 0x2000);

            var value = machine.ReadNextPCWord();

            Assert.Equal(0x0102, value);
        }

        [Fact]
        public void FetchInstruction_ShouldFetchValidInstruction()
        {
            Machine machine = new();
            InstructionDecoder decoder = new (machine);
            var _ = machine.LoadExecutableData([0x30, 0x3C, 0x00, 0x32], 0x2000);      // instruction is: move.w #50,d0

            var instruction = decoder.FetchInstruction();

            Assert.NotNull(instruction);
            Assert.Equal(0x303C, instruction.Opcode);
            Assert.Equal(OpSize.Word, instruction.Size);
            Assert.Equal((byte)AddrMode.Immediate, instruction!.SourceAddrMode!.Value);
            Assert.Equal(0x0032, instruction!.SourceExtWord1!.Value);
            Assert.Equal((byte)0, instruction!.DestAddrMode!.Value);
            Assert.Equal(OpHandlerID.MOVE, instruction.Info.HandlerID);
        }

    }
}
