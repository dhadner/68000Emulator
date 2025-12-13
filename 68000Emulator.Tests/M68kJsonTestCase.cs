using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PendleCodeMonkey.MC68000Emulator.Tests
{
    public class M68KJsonTestCase
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("initial")]
        public M68KTestCaseState Initial { get; set; } = new();

        [JsonPropertyName("final")]
        public M68KTestCaseState Final { get; set; } = new();

        [JsonPropertyName("length")]
        public uint Length { get; set; }

        [JsonPropertyName("transactions")]
        [JsonConverter(typeof(M68KJsonTransactionsConverter))]
        public List<M68KJsonTransaction> Transactions { get; set; } = [];
    }

    public class M68KTestCaseState
    {
        [JsonPropertyName("d0")]
        public uint D0 { get; set; }

        [JsonPropertyName("d1")]
        public uint D1 { get; set; }

        [JsonPropertyName("d2")]
        public uint D2 { get; set; }

        [JsonPropertyName("d3")]
        public uint D3 { get; set; }

        [JsonPropertyName("d4")]
        public uint D4 { get; set; }

        [JsonPropertyName("d5")]
        public uint D5 { get; set; }

        [JsonPropertyName("d6")]
        public uint D6 { get; set; }

        [JsonPropertyName("d7")]
        public uint D7 { get; set; }

        [JsonPropertyName("a0")]
        public uint A0 { get; set; }

        [JsonPropertyName("a1")]
        public uint A1 { get; set; }

        [JsonPropertyName("a2")]
        public uint A2 { get; set; }

        [JsonPropertyName("a3")]
        public uint A3 { get; set; }

        [JsonPropertyName("a4")]
        public uint A4 { get; set; }

        [JsonPropertyName("a5")]
        public uint A5 { get; set; }

        [JsonPropertyName("a6")]
        public uint A6 { get; set; }

        [JsonPropertyName("usp")]
        public uint Usp { get; set; }

        [JsonPropertyName("ssp")]
        public uint Ssp { get; set; }

        [JsonPropertyName("sr")]
        public ushort Sr { get; set; }

        [JsonPropertyName("pc")]
        public uint Pc { get; set; }

        [JsonPropertyName("prefetch")]
        public List<ushort> Prefetch { get; set; } = new List<ushort>();

        [JsonPropertyName("ram")]
        public List<M68KJsonRamEntry> Ram { get; set; } = new List<M68KJsonRamEntry>();
    }

    [JsonConverter(typeof(M68KJsonRamEntryConverter))]
    public class M68KJsonRamEntry
    {
        public uint Address { get; set; }
        public byte Data { get; set; }
    }

    public class M68KJsonRamEntryConverter : JsonConverter<M68KJsonRamEntry>
    {
        public override M68KJsonRamEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("Expected start of array for RAM entry.");
            }
            reader.Read();

            var ramEntry = new M68KJsonRamEntry();

            if (reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException("Expected number for RAM address.");
            }
            ramEntry.Address = reader.GetUInt32();
            reader.Read();

            if (reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException("Expected number for RAM data.");
            }
            ramEntry.Data = reader.GetByte();
            reader.Read();

            if (reader.TokenType != JsonTokenType.EndArray)
            {
                throw new JsonException("Expected end of array for RAM entry.");
            }

            return ramEntry;
        }

        public override void Write(Utf8JsonWriter writer, M68KJsonRamEntry value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.Address);
            writer.WriteNumberValue(value.Data);
            writer.WriteEndArray();
        }
    }

    public class M68KJsonTransaction
    {
        public string Type { get; set; } = "";
        public uint? Cycles { get; set; }
        public byte? FunctionCode { get; set; }
        public uint? Address { get; set; }
        public string? Size { get; set; } // ".b", ".w"
        public ushort? Data { get; set; }
        public bool? UDS { get; set; }
        public bool? LDS { get; set; }

        public override string ToString()
        {
            if (Type == "n")
            {
                return $"Type: {Type,-2}, Cycles: {Cycles,2}";
            }

            string fc = FunctionCode!.Value switch
            {
                0 => "0b000 (Undefined),",
                1 => "0b001 (User Data),",
                2 => "0b010 (User Program),",
                3 => "0b011 (Reserved),",
                4 => "0b100 (Undefined),",
                5 => "0b101 (Supervisor Data),",
                6 => "0b110 (Supervisor Program),",
                7 => "0b111 (CPU Space),",
                _ => $"{FunctionCode} (Unknown/Error),"
            };
            return $"Type: {Type,-2}, Cycles: {Cycles,2}, FunctionCode: {fc,-27} Address: ${Address:x8}, Size: {Size,2}, Data: ${Data:x4}, UDS: {UDS,-5}, LDS: {LDS,-5}";
        }
    }

    public class M68KJsonTransactionsConverter : JsonConverter<List<M68KJsonTransaction>>
    {
        public override List<M68KJsonTransaction> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException();
            }

            var transactions = new List<M68KJsonTransaction>();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return transactions;
                }

                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    M68KJsonTransaction transaction = new();
                    reader.Read(); // Move to first element
                    transaction.Type = reader.GetString() ?? "UNKNOWN";

                    if (transaction.Type == "n")
                    {
                        reader.Read();
                        transaction.Cycles = reader.GetUInt32();
                    }
                    else
                    {
                        reader.Read();
                        transaction.Cycles = reader.GetUInt32();
                        reader.Read();
                        transaction.FunctionCode = reader.GetByte();
                        reader.Read();
                        transaction.Address = reader.GetUInt32();
                        reader.Read();
                        transaction.Size = reader.GetString();
                        reader.Read();
                        transaction.Data = reader.GetUInt16();
                        if (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                        {
                            transaction.UDS = reader.GetInt64() != 0;
                            if (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                            {
                                transaction.LDS = reader.GetInt64() != 0;
                            }
                        }
                    }

                    while (reader.TokenType != JsonTokenType.EndArray)
                    {
                        reader.Read();
                    }
                    transactions.Add(transaction);
                }
            }

            return transactions;
        }

        public override void Write(Utf8JsonWriter writer, List<M68KJsonTransaction> value, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }
    }
}