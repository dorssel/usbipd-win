// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Usbipd.Automation;

public class NullableBusIdJsonConverter : JsonConverter<BusId?>
{
    public override BusId? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.GetString() is not string text)
        {
            return null;
        }

        return BusId.Parse(text);
    }

    public override void Write(Utf8JsonWriter writer, BusId? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.ToString());
    }
}
