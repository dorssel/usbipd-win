// SPDX-FileCopyrightText: 2023 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace UsbIds;

[Generator]
#pragma warning disable CA1812
sealed class UsbIdsSourceGenerator : IIncrementalGenerator
#pragma warning restore CA1812
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterImplementationSourceOutput(context.AdditionalTextsProvider.Where(static f => Path.GetFileName(f.Path) == "usb.ids"), Execute);
    }

    public static void Execute(SourceProductionContext context, AdditionalText additionalText)
    {
        if (additionalText.GetText() is not SourceText sourceText)
        {
            throw new InvalidDataException("unable to read usb.ids");
        }

        var vendors = new SortedDictionary<ushort, (string Name, SortedDictionary<ushort, string> Products)>();
        SortedDictionary<ushort, string>? vendor = null;

        foreach (var line in sourceText.Lines)
        {
            var text = sourceText.ToString(line.Span).TrimEnd();
            if (string.IsNullOrEmpty(text) || text.StartsWith("#"))
            {
                // empty line or comment does not change context
                continue;
            }

            var match = Regex.Match(text, "^([0-9a-fA-F]{4}) *(.*)$");
            if (match.Success)
            {
                // new vendor
                var vid = ushort.Parse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber);
                var vendorName = match.Groups[2].Value.Trim();
                if (string.IsNullOrEmpty(vendorName))
                {
                    throw new InvalidDataException($"vendor id {vid:x4} without name in usb.ids");
                }
                if (vendors.ContainsKey(vid))
                {
                    throw new InvalidDataException($"duplicate vendor id {vid:x4} in usb.ids");
                }
                vendor = [];
                vendors.Add(vid, (vendorName, vendor));
                continue;
            }

            if (vendor == null)
            {
                // no vendor context --> we're done
                continue;
            }

            match = Regex.Match(text, "^\\t([0-9a-fA-F]{4}) *(.*)$");
            if (match.Success)
            {
                // new product
                var pid = ushort.Parse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber);
                var productName = match.Groups[2].Value.Trim();
                if (string.IsNullOrEmpty(productName))
                {
                    throw new InvalidDataException($"product id {pid:x4} without name in usb.ids");
                }
                if (vendor.ContainsKey(pid))
                {
                    throw new InvalidDataException($"duplicate product id {pid:x4} in usb.ids");
                }
                vendor.Add(pid, productName);
                continue;
            }

            if (text.StartsWith("\t\t"))
            {
                // we don't deal with interfaces
                continue;
            }

            if (text.StartsWith("\t"))
            {
                // in vendor context, and not double-tab: this should have parsed as a product earlier
                throw new InvalidDataException($"parse error for '{text}' while in vendor context in usb.ids");
            }

            // anything but # or \t at start --> end of vendor context
            vendor = null;
        }

        if (vendors.Count == 0)
        {
            throw new InvalidDataException("no vendors found in usb.ids");
        }
        if (vendors.All(vendor => vendor.Value.Products.Count == 0))
        {
            throw new InvalidDataException("no products found in usb.ids");
        }

        // Build a concatenation of unique strings, separated by a NUL character.
        //      We only want a single static string variable into which we offset individual strings.
        // Build a lookup table for vendors:
        //      Each lookup entry consists of (ulong)<VID><0000><32-bit string start offset>.
        // Build a lookup table for products:
        //      Each lookup entry consists of (ulong)<VID><PID><32-bit string start offset>.
        // Rationale: Very fast during initialization and great input for a binary search.

        var offsets = new SortedDictionary<string, int>();
        var strings = new StringBuilder();
        var vendorLookup = new StringBuilder();
        var productLookup = new StringBuilder();

        int GetOffset(string s)
        {
            if (offsets.TryGetValue(s, out var offset))
            {
                return offset;
            }
            offset = strings.Length;
            offsets.Add(s, offset);
            _ = strings.Append(s).Append('\0');
            return offset;
        }

        foreach (var v in vendors)
        {
            var offset = GetOffset(v.Value.Name);
            _ = vendorLookup.Append($$"""
                        0x{{v.Key:x4}}0000{{offset:x8}},

                """);

            foreach (var p in v.Value.Products)
            {
                offset = GetOffset(p.Value);
                _ = productLookup.Append($$"""
                        0x{{v.Key:x4}}{{p.Key:x4}}{{offset:x8}},

                """);
            }
        }

        // Build up the source code
        var source = $$"""
            // <auto-generated/>
            using System;

            namespace Usbipd.Automation;

            static partial class UsbIds
            {
                static readonly string Strings = {{SymbolDisplay.FormatLiteral(strings.ToString(), true)}};

                static readonly ulong[] VendorLookup = {
            {{vendorLookup}}
                };

                static readonly ulong[] ProductLookup = {
            {{productLookup}}
                };
            }

            """;

        // Add the source code to the compilation
        context.AddSource($"UsbIds.g.cs", source);
    }
}
