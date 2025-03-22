// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

#if !NETSTANDARD

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Usbipd.Automation;

class JsonConverterIPAddress : JsonConverter<IPAddress>
{
    public override IPAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.GetString() is string text ? IPAddress.Parse(text) : throw new InvalidDataException();
    }

    public override void Write(Utf8JsonWriter writer, IPAddress value, JsonSerializerOptions options)
    {
        _ = writer ?? throw new ArgumentNullException(nameof(writer));
        _ = value ?? throw new ArgumentNullException(nameof(value));

        writer.WriteStringValue(value.ToString());
    }
}

#endif
