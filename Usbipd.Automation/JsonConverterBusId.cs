// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

#if !NETSTANDARD

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Usbipd.Automation;

public class JsonConverterBusId : JsonConverter<BusId>
{
    public override BusId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.GetString() is not string text)
        {
            throw new InvalidDataException();
        }

        return BusId.Parse(text);
    }

    public override void Write(Utf8JsonWriter writer, BusId value, JsonSerializerOptions options)
    {
        _ = writer ?? throw new ArgumentNullException(nameof(writer));

        writer.WriteStringValue(value.ToString());
    }
}

#endif
