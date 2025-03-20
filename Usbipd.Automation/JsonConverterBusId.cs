// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

#if !NETSTANDARD

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Usbipd.Automation;

class JsonConverterBusId : JsonConverter<BusId>
{
    public override BusId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.GetString() is string text ? BusId.Parse(text) : throw new InvalidDataException();
    }

    public override void Write(Utf8JsonWriter writer, BusId value, JsonSerializerOptions options)
    {
        _ = writer ?? throw new ArgumentNullException(nameof(writer));

        writer.WriteStringValue(value.ToString());
    }
}

#endif
