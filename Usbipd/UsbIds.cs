//#define USBIDS_DBG_VERBOSE
#define USBIDS_STOPWATCH

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Usbipd
{
    /// <summary>
    /// A C# port of https://github.com/cezanne/usbip-win/blob/master/userspace/lib/names.c (GPLv2+)
    /// </summary>
    class UsbIds
    {
        static UsbIds()
        {
            Load();
        }

        class UsbVendor
        {
            public UsbVendor(string name, uint vendorid)
            {
                this.name = name;
                this.vendorid = vendorid;
            }

            public string name { get; private set; }
            public uint vendorid { get; private set; }
            public Dictionary<uint, UsbProduct> products = new Dictionary<uint, UsbProduct>();
        }

        class UsbProduct
        {
            public uint vendorid { get; private set; }
            public uint productid { get; private set; }
            public string name { get; private set; }

            public UsbProduct(string name, uint vendorid, uint productid)
            {
                this.name = name;
                this.vendorid = vendorid;
                this.productid = productid;
            }
        }

        class UsbClass
        {
            public UsbClass(string name, uint classid)
            {
                this.name = name;
                this.classid = classid;
            }

            public string name { get; private set; }
            public uint classid { get; private set; }
            public Dictionary<uint, UsbSubclass> subclasses = new Dictionary<uint, UsbSubclass>();
        }

        class UsbSubclass
        {
            public UsbSubclass(string name, uint classid, uint subclassid)
            {
                this.name = name;
                this.classid = classid;
                this.subclassid = subclassid;
            }

            public uint classid { get; private set; }
            public uint subclassid { get; private set; }
            public string name { get; private set; }
            public Dictionary<uint, UsbClassProtocol> protocols = new Dictionary<uint, UsbClassProtocol>();
        }

        class UsbClassProtocol
        {
            public UsbClassProtocol(string name, uint classid, uint subclassid, uint protocolid)
            {
                this.name = name;
                this.classid = classid;
                this.subclassid = subclassid;
                this.protocolid = protocolid;
            }

            public uint classid { get; private set; }
            public uint subclassid { get; private set; }
            public uint protocolid { get; private set; }
            public string name { get; private set; }
        }

        private static Dictionary<uint, UsbVendor> vendors = new Dictionary<uint, UsbVendor>();
        private static Dictionary<uint, UsbClass> classes = new Dictionary<uint, UsbClass>();

        private static bool isxdigit(char c)
        {
            return c >= '0' && c <= '9' || ('a' <= c && c <= 'f') || ('A' <= c && c <= 'F');
        }

        private static bool isspace(char c)
        {
            return c == ' ';
        }

        private static bool new_vendor(string name, uint vendorid)
        {
            return vendors.TryAdd(vendorid, new UsbVendor(name, vendorid));
        }

        public static string GetVendorName(uint vendorid)
        {
            vendors.TryGetValue(vendorid, out var usbvendor);
            return usbvendor?.name ?? String.Empty;
        }

        private static bool new_product(string name, uint vendorid, uint productid)
        {
            var usbvendor = vendors[vendorid];
            return usbvendor.products.TryAdd(productid, new UsbProduct(name, vendorid, productid));
        }

        public static string GetProductName(uint vendorid, uint productid, string defaultValue)
        {
            UsbProduct? usbproduct = null;
            if (!vendors.TryGetValue(vendorid, out var usbvendor))
            {
                return defaultValue;
            }
            if (!usbvendor.products.TryGetValue(productid, out usbproduct))
            {
                return String.Format("{0} {1}", usbvendor.name, defaultValue);
            }
            return String.Format("{0} {1}", usbvendor.name, usbproduct.name);
        }

        private static bool new_class(string name, uint classid)
        {
            return classes.TryAdd(classid, new UsbClass(name, classid));
        }

        private static bool new_subclass(string name, uint classid, uint subclassid)
        {
            var usbclass = classes[classid];
            return usbclass.subclasses.TryAdd(subclassid, new UsbSubclass(name, classid, subclassid));
        }

        private static bool new_protocol(string name, uint classid, uint subclassid, uint protocolid)
        {
            var usbclass = classes[classid];
            var usbsubclass = usbclass.subclasses[subclassid];
            return usbsubclass.protocols.TryAdd(protocolid, new UsbClassProtocol(name, classid, subclassid, protocolid));
        }

        private static void dbg(string format, params object?[] args)
        {
            Debug.WriteLine(String.Format(format, args));
        }

        private static uint Load()
        {
            dbg("+UsbDevice.Load()");
#if USBIDS_STOPWATCH
            var stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();
#endif
            uint numlines = 0;
            var filename = "usb.ids";
            if (File.Exists(filename))
            {
                using (var f = File.OpenText(filename))
                {
                    int lastvendor = -1;
                    int lastclass = -1;
                    int lastsubclass = -1;
                    int lasthut = -1;
                    int lastlang = -1;

                    int numvendors = 0;
                    int numproducts = 0;
                    int numclasses = 0;
                    int numsubclasses = 0;
                    int numprotocols = 0;

                    var patternVendor = @"\A(?'id'[0-9a-f]{4}) +(?'name'.*)\Z";
                    var patternProduct = @"\A\t(?'id'[0-9a-f]{4}) +(?'name'.*)\Z";
                    var patternClass = @"\AC (?'id'[0-9a-f]{2}) +(?'name'.*)\Z";
                    var patternSubclass = @"\A\t(?'id'[0-9a-f]{2}) +(?'name'.*)\Z";
                    var patternProtocol = @"\A\t\t(?'id'[0-9a-f]{2}) +(?'name'.*)\Z";
                    var patternHidUsage = @"\AHUT (?'id'[0-9a-f]{2}) +(?'name'.*)\Z";
                    var patternLang = @"\AL (?'id'[0-9a-f]{4}) +(?'name'.*)\Z";
                    Match match;

                    string? buf;
                    while ((buf = f.ReadLine()) != null)
                    {
                        numlines++;

                        if (buf.Length < 1 || buf[0] == '#')
                        {
                            lastvendor = lastclass = lastsubclass = lasthut = lastlang = -1;
                            continue;
                        }

                        match = Regex.Match(buf, patternVendor);
                        if (match.Success)
                        {
                            var id = Convert.ToUInt32(match.Groups["id"].Value, 16);
                            var name = match.Groups["name"].Value;
#if USBIDS_DBG_VERBOSE
                            dbg("{0:D5}:   vendor id={1:X4}, name=\"{2}\"", numlines, id, name);
#endif
                            if (new_vendor(name, id))
                            {
                                ++numvendors;
                            }
                            else
                            {
                                dbg("Duplicate vendor spec at line {0:D5} vendor {1:X4} {2}",
                                    numlines, id, name);
                            }
                            lastvendor = (int)id;
                            lasthut = lastlang = lastclass = lastsubclass = -1;
                            continue;
                        }
                        match = Regex.Match(buf, patternProduct);
                        if (match.Success && lastvendor != -1)
                        {
                            var id = Convert.ToUInt32(match.Groups["id"].Value, 16);
                            var name = match.Groups["name"].Value;
#if USBIDS_DBG_VERBOSE
                            dbg("{0:D5}:  product id={1:X4}, name=\"{2}\"", numlines, id, name);
#endif
                            if (new_product(name, (uint)lastvendor, id))
                            {
                                ++numproducts;
                            }
                            else
                            {
                                dbg("Duplicate product spec at line {0:D5} product {1:X4}:{2:X4} {3}",
                                    numlines, lastvendor, id, name);
                            }
                            continue;
                        }
                        match = Regex.Match(buf, patternClass);
                        if (match.Success)
                        {
                            var id = Convert.ToUInt32(match.Groups["id"].Value, 16);
                            var name = match.Groups["name"].Value;
#if USBIDS_DBG_VERBOSE
                            dbg("{0:D5}:    class id=  {1:X2}, name=\"{2}\"", numlines, id, name);
#endif
                            if (new_class(name, id))
                            {
                                ++numclasses;
                            }
                            else
                            {
                                dbg("Duplicate class spec at line {0:D5} class {1:X2} {2}",
                                    numlines, id, name);
                            }
                            lastclass = (int)id;
                            lasthut = lastlang = lastvendor = lastsubclass = -1;
                            continue;
                        }
                        match = Regex.Match(buf, patternSubclass);
                        if (match.Success && lastclass != -1)
                        {
                            var id = Convert.ToUInt32(match.Groups["id"].Value, 16);
                            var name = match.Groups["name"].Value;
#if USBIDS_DBG_VERBOSE
                            dbg("{0:D5}: subclass id=  {1:X2}, name=\"{2}\"", numlines, id, name);
#endif
                            if (new_subclass(name, (uint)lastclass, id))
                            {
                                ++numsubclasses;
                            }
                            else
                            {
                                dbg("Duplicate subclass spec at line {0:D5} class {1:X2}:{2:X2} {3}",
                                    numlines, lastclass, id, name);
                            }
                            lastsubclass = (int)id;
                            continue;
                        }
                        match = Regex.Match(buf, patternProtocol);
                        if (match.Success && lastclass != -1 && lastsubclass != -1)
                        {
                            var id = Convert.ToUInt32(match.Groups["id"].Value, 16);
                            var name = match.Groups["name"].Value;
#if USBIDS_DBG_VERBOSE
                            dbg("{0:D5}: protocol id=  {1:X2}, name=\"{2}\"", numlines, id, name);
#endif
                            if (new_protocol(name, (uint)lastclass, (uint)lastsubclass, id))
                            {
                                ++numprotocols;
                            }
                            else
                            {
                                dbg("Duplicate protocol spec at line {0:D5} class {1:X2}:{2:X2}:{3:X2} {4}",
                                    numlines, lastclass, lastsubclass, id, name);
                            }
                            continue;
                        }
                        match = Regex.Match(buf, patternHidUsage);
                        if (match.Success)
                        {
                            /*
                             * set 1 as pseudo-id to indicate that the parser is
                             * in a `HUT' section.
                             */
                            lasthut = 1;
                            lastlang = lastclass = lastvendor = lastsubclass = -1;
                            continue;
                        }
                        match = Regex.Match(buf, patternLang);
                        if (match.Success)
                        {
                            /*
                             * set 1 as pseudo-id to indicate that the parser is
                             * in a `L' section.
                             */
                            lastlang = 1;
                            lasthut = lastclass = lastvendor = lastsubclass = -1;
                            continue;
                        }
                    } // while
                    dbg($"UsbDevice.Load() DONE: numvendors={numvendors}, numproducts={numproducts}, numclasses={numclasses}, numsubclasses={numsubclasses}, numprotocols={numprotocols}");
                } // using
            } // exists
#if USBIDS_STOPWATCH
            stopwatch.Stop();
            dbg($"-UsbDevice.Load(); numlines={numlines}; took {stopwatch.ElapsedMilliseconds} ms");
#else
            dbg($"-UsbDevice.Load(); numlines={numlines}");
#endif
            return numlines;
        }
    }
}
