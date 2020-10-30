<?xml version="1.0" encoding="UTF-8"?>
<!--
    usbipd-win
    Copyright (C) 2020  Frans van Dorsselaer

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
-->
<xsl:stylesheet
  xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
  xmlns:wix="http://schemas.microsoft.com/wix/2006/wi"
  xmlns="http://schemas.microsoft.com/wix/2006/wi"
  version="1.0"
  exclude-result-prefixes="xsl">

    <!-- copy everything -->
    <xsl:template match="@*|node()">
        <xsl:copy>
            <xsl:apply-templates select="@*|node()"/>
        </xsl:copy>
    </xsl:template>

    <!-- but remove UsbIpServer.exe -->
    <xsl:template match= "//wix:Component[wix:File/@Source = '$(var.PublishDir)\UsbIpServer.exe']" />
    <xsl:key name="ComponentsToSuppress" match="wix:Component[wix:File/@Source = '$(var.PublishDir)\UsbIpServer.exe']" use="@Id" />
    <xsl:template match="wix:ComponentRef[key('ComponentsToSuppress', @Id)]" />

</xsl:stylesheet>
