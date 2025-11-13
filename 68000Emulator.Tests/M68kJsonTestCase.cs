using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PendleCodeMonkey.MC68000Emulator.Tests;

public class M68kTestcaseState
{
    [JsonPropertyName("d0")] public uint D0 { get; set; }
    [JsonPropertyName("d1")] public uint D1 { get; set; }
    [JsonPropertyName("d2")] public uint D2 { get; set; }
    [JsonPropertyName("d3")] public uint D3 { get; set; }
    [JsonPropertyName("d4")] public uint D4 { get; set; }
    [JsonPropertyName("d5")] public uint D5 { get; set; }
    [JsonPropertyName("d6")] public uint D6 { get; set; }
    [JsonPropertyName("d7")] public uint D7 { get; set; }
    [JsonPropertyName("a0")] public uint A0 { get; set; }
    [JsonPropertyName("a1")] public uint A1 { get; set; }
    [JsonPropertyName("a2")] public uint A2 { get; set; }
    [JsonPropertyName("a3")] public uint A3 { get; set; }
    [JsonPropertyName("a4")] public uint A4 { get; set; }
    [JsonPropertyName("a5")] public uint A5 { get; set; }
    [JsonPropertyName("a6")] public uint A6 { get; set; }
    [JsonPropertyName("usp")] public uint Usp { get; set; }
    [JsonPropertyName("ssp")] public uint Ssp { get; set; }
    [JsonPropertyName("sr")] public ushort Sr { get; set; }
    [JsonPropertyName("pc")] public uint Pc { get; set; }
    [JsonPropertyName("ram")] public List<List<long>> Ram { get; set; } = new();
}

public class M68kJsonTestCase
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("initial")]
    public M68kTestcaseState Initial { get; set; } = new();

    [JsonPropertyName("final")]
    public M68kTestcaseState Final { get; set; } = new();
}
