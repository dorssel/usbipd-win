<?xml version="1.0" encoding="UTF-8"?>
<!--
SPDX-FileCopyrightText: 2020 Frans van Dorsselaer

SPDX-License-Identifier: GPL-2.0-only
-->
<xsl:stylesheet
  xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
  xmlns:wix="http://schemas.microsoft.com/wix/2006/wi"
  version="1.0"
  exclude-result-prefixes="xsl">

    <!-- copy everything -->
    <xsl:template match="@*|node()">
        <xsl:copy>
            <xsl:apply-templates select="@*|node()"/>
        </xsl:copy>
    </xsl:template>

    <!-- but remove usbipd.exe -->
    <xsl:template match= "//wix:Component[wix:File/@Source = '$(var.PublishDir)\usbipd.exe']" />
    <xsl:key name="ComponentsToSuppress" match="wix:Component[wix:File/@Source = '$(var.PublishDir)\usbipd.exe']" use="@Id" />
    <xsl:template match="wix:ComponentRef[key('ComponentsToSuppress', @Id)]" />

</xsl:stylesheet>
