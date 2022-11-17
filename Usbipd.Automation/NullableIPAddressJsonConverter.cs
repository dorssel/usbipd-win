// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Usbipd.Automation;

public class NullableIPAddressJsonConverter : JsonConverter<IPAddress?>
{
    public override IPAddress? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.GetString() is not string text)
        {
            return null;
        }

        return IPAddress.Parse(text);
    }

    public override void Write(Utf8JsonWriter writer, IPAddress? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }

        writer.WriteStringValue(value?.ToString());
    }
}
