// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace UnitTests;

static class JsonHelpers
{
    public static string DataContractSerialize<T>(T value)
    {
        using var memoryStream = new MemoryStream();
        {
            using var writer = JsonReaderWriterFactory.CreateJsonWriter(memoryStream, Encoding.UTF8, false, true);
            var serializer = new DataContractJsonSerializer(value!.GetType());
            serializer.WriteObject(writer, value);
        }
        return Encoding.UTF8.GetString(memoryStream.ToArray());
    }

    public static string TextJsonSerialize<T>(T value, JsonTypeInfo<T> jsonTypeInfo)
    {
        using var memoryStream = new MemoryStream();
        {
            using var jsonWriter = new Utf8JsonWriter(memoryStream, new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = true });
            JsonSerializer.Serialize(jsonWriter, value, jsonTypeInfo);
        }
        return Encoding.UTF8.GetString(memoryStream.ToArray());
    }

    public static string NormalizePretty(string json)
    {
        // To handle the differences in DataContract, JsonSerializer, and source code literals.
        // We normalize towards source code literals.

        // - make indent 4 instead of 2
        // - make empty arrays [] instead of [ ]
        // - make line ending \r\n
        // - remove final newline

        var firstLine = true;
        var builder = new StringBuilder();
        foreach (var lineIn in json.Split("\n"))
        {
            var lineOut = lineIn.TrimEnd();
            if (firstLine)
            {
                firstLine = false;
            }
            else
            {
                _ = builder.Append("\r\n");
            }
            lineOut = lineOut.Replace("[ ]", "[]");
            var extraIndent = lineOut.TakeWhile(c => c == ' ').ToArray();

            _ = builder.Append(extraIndent).Append(lineOut);
        }
        return builder.ToString();
    }
}
