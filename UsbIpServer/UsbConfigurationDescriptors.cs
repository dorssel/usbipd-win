// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using Windows.Win32;
using Windows.Win32.Devices.Usb;

using static UsbIpServer.Tools;

namespace UsbIpServer
{
    sealed class UsbConfigurationDescriptors
    {
        sealed class UsbEndpoint
        {
            public UsbEndpoint(USB_ENDPOINT_DESCRIPTOR descriptor)
            {
                Descriptor = descriptor;
            }

            public byte TransferType => (byte)(Descriptor.bmAttributes & 0x03);

            public USB_ENDPOINT_DESCRIPTOR Descriptor { get; }
        }

        sealed class UsbAlternateInterface
        {
            public UsbAlternateInterface(USB_INTERFACE_DESCRIPTOR descriptor)
            {
                Descriptor = descriptor;
            }

            /// <summary>
            /// The index includes the direction (high bit), which for control endpoints is always unset.
            /// </summary>
            public SortedDictionary<byte, UsbEndpoint> Endpoints { get; } = new();

            public USB_INTERFACE_DESCRIPTOR Descriptor { get; }
        }

        sealed class UsbInterface
        {
            public SortedDictionary<byte, UsbAlternateInterface> Alternates { get; } = new();

            byte CurrentAlternate;

            public void SetAlternate(byte alternateSetting)
            {
                if (!Alternates.ContainsKey(alternateSetting))
                {
                    throw new ArgumentOutOfRangeException(nameof(alternateSetting));
                }
                CurrentAlternate = alternateSetting;
            }

            public UsbAlternateInterface Current => Alternates[CurrentAlternate];
        }

        sealed class UsbConfiguration
        {
            public UsbConfiguration(USB_CONFIGURATION_DESCRIPTOR descriptor)
            {
                Descriptor = descriptor;
            }

            public SortedDictionary<byte, UsbInterface> Interfaces { get; } = new();

            public USB_CONFIGURATION_DESCRIPTOR Descriptor { get; }
        }

        SortedDictionary<byte, UsbConfiguration> Configurations { get; set; } = new();

        /// <summary>
        /// special value: 0 -> low power mode, not an actual configuration
        /// </summary>
        byte CurrentConfiguration;

        public void AddDescriptor(ReadOnlySpan<byte> descriptor)
        {
            var offset = 0;
            UsbConfiguration? configuration = null;
            UsbAlternateInterface? alternateInterface = null;
            while (offset != descriptor.Length)
            {
                BytesToStruct(descriptor[offset..], out USB_COMMON_DESCRIPTOR common);
                switch ((uint)common.bDescriptorType)
                {
                    case Constants.USB_CONFIGURATION_DESCRIPTOR_TYPE:
                        if (configuration is not null)
                        {
                            throw new ArgumentException("duplicate USB_CONFIGURATION_DESCRIPTOR_TYPE");
                        }
                        BytesToStruct(descriptor[offset..], out USB_CONFIGURATION_DESCRIPTOR config);
                        configuration = new(config);
                        if (!Configurations.TryAdd(config.bConfigurationValue, configuration))
                        {
                            Console.Error.WriteLine($"WARNING: bConfigurationValue {config.bConfigurationValue} is duplicated. ignored.");
                        }
                        break;
                    case Constants.USB_INTERFACE_DESCRIPTOR_TYPE:
                        if (configuration is null)
                        {
                            throw new ArgumentException("expected USB_CONFIGURATION_DESCRIPTOR_TYPE");
                        }
                        BytesToStruct(descriptor[offset..], out USB_INTERFACE_DESCRIPTOR iface);
                        if (iface.bAlternateSetting == 0)
                        {
                            configuration.Interfaces[iface.bInterfaceNumber] = new();
                        }
                        alternateInterface = new(iface);
                        configuration.Interfaces[iface.bInterfaceNumber].Alternates[iface.bAlternateSetting] = alternateInterface;
                        break;
                    case Constants.USB_ENDPOINT_DESCRIPTOR_TYPE:
                        if (alternateInterface is null)
                        {
                            throw new ArgumentException("expected USB_INTERFACE_DESCRIPTOR_TYPE");
                        }
                        BytesToStruct(descriptor[offset..], out USB_ENDPOINT_DESCRIPTOR ep);
                        var endpoint = new UsbEndpoint(ep);
                        switch ((uint)endpoint.TransferType)
                        {
                            case Constants.USB_ENDPOINT_TYPE_CONTROL:
                                alternateInterface.Endpoints.Add((byte)(ep.bEndpointAddress & 0x0f), endpoint);
                                break;
                            default:
                                alternateInterface.Endpoints.Add((byte)(ep.bEndpointAddress & 0x8f), endpoint);
                                break;
                        }
                        break;
                }
                offset += common.bLength;
            }
        }

        public void SetConfiguration(byte configurationValue)
        {
            if (configurationValue > 0)
            {
                if (!Configurations.ContainsKey(configurationValue))
                {
                    throw new ArgumentOutOfRangeException(nameof(configurationValue));
                }

                // USB spec: setting the configuration resets everything to defaults, even if the configuration index does not change
                var configuration = Configurations[configurationValue];
                foreach (var iface in configuration.Interfaces.Values)
                {
                    iface.SetAlternate(0);
                }
            }

            CurrentConfiguration = configurationValue;

            RefreshEndpointCache();
        }

        public void SetInterface(byte interfaceNumber, byte alternateSetting)
        {
            var configuration = Configurations[CurrentConfiguration];
            if (!configuration.Interfaces.ContainsKey(interfaceNumber))
            {
                throw new ArgumentOutOfRangeException(nameof(interfaceNumber));
            }
            configuration.Interfaces[interfaceNumber].SetAlternate(alternateSetting);

            RefreshEndpointCache();
        }

        void RefreshEndpointCache()
        {
            EndpointCache.Clear();

            if (CurrentConfiguration >= 0)
            {
                foreach (var iface in Configurations[CurrentConfiguration].Interfaces.Values)
                {
                    var alternate = iface.Current;
                    foreach (var endpoint in alternate.Endpoints.Values)
                    {
                        switch ((uint)endpoint.TransferType)
                        {
                            case Constants.USB_ENDPOINT_TYPE_CONTROL:
                                // for easy cache lookup, control endpoints are registered as both input and output
                                EndpointCache[(byte)(endpoint.Descriptor.bEndpointAddress & 0x0f)] = endpoint;
                                EndpointCache[(byte)((endpoint.Descriptor.bEndpointAddress & 0x0f) | 0x80)] = endpoint;
                                break;
                            default:
                                EndpointCache[(byte)(endpoint.Descriptor.bEndpointAddress & 0x8f)] = endpoint;
                                break;
                        }
                    }
                }
            }
        }

        SortedDictionary<byte, UsbEndpoint> EndpointCache { get; } = new();

        public uint GetEndpointType(uint endpoint, bool input)
        {
            if (endpoint == 0)
            {
                // every configuration (even low-power) always has an "endpoint 0" which is the default control pipe (input and output) 
                return Constants.USB_ENDPOINT_TYPE_CONTROL;
            }
            else if (endpoint > 0x0f)
            {
                throw new ArgumentOutOfRangeException(nameof(endpoint));
            }

            var index = (byte)endpoint;
            if (input)
            {
                index |= 0x80;
            }
            return EndpointCache[index].TransferType;
        }

        public (byte Class, byte SubClass, byte Protocol)[] GetUniqueInterfaces()
        {
            var result = new List<(byte, byte, byte)>();

            foreach (var configuration in Configurations.Values)
            {
                foreach (var iface in configuration.Interfaces.Values)
                {
                    foreach (var alternate in iface.Alternates.Values)
                    {
                        var value = (alternate.Descriptor.bInterfaceClass, alternate.Descriptor.bInterfaceSubClass, alternate.Descriptor.bInterfaceProtocol);
                        if (!result.Contains(value))
                        {
                            result.Add(value);
                        }
                    }
                }
            }

            return result.ToArray();
        }
    }
}
