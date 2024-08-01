// SPDX-FileCopyrightText: 2024 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

// This file is compiled only when building from Visual Studio, which is incompatible with GitVersion 6.
// To get accurate versioning, build from the command line with 'dotnet build' instead.

using System.Diagnostics.CodeAnalysis;

[ExcludeFromCodeCoverage]
static class GitVersionInformation
{
    public static string Major = "0";
    public static string Minor = "9";
    public static string Patch = "99";
    public static string PreReleaseTag = "";
    public static string PreReleaseTagWithDash = "";
    public static string PreReleaseLabel = "";
    public static string PreReleaseLabelWithDash = "";
    public static string PreReleaseNumber = "";
    public static string WeightedPreReleaseNumber = "60000";
    public static string BuildMetaData = "999";
    public static string BuildMetaDataPadded = "0999";
    public static string FullBuildMetaData = "999.Branch.vs.Sha.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public static string MajorMinorPatch = "0.9.99";
    public static string SemVer = "0.9.99";
    public static string LegacySemVer = "0.9.99";
    public static string LegacySemVerPadded = "0.9.99";
    public static string AssemblySemVer = "0.9.99.0";
    public static string AssemblySemFileVer = "0.9.99.0";
    public static string FullSemVer = "0.9.99+999";
    public static string InformationalVersion = "0.9.99+999.Branch.vs.Sha.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public static string BranchName = "vs";
    public static string EscapedBranchName = "vs";
    public static string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"; // DevSkim: ignore DS173237
    public static string ShortSha = "aaaaaaa";
    public static string NuGetVersionV2 = "0.9.99";
    public static string NuGetVersion = "0.9.99";
    public static string NuGetPreReleaseTagV2 = "";
    public static string NuGetPreReleaseTag = "";
    public static string VersionSourceSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"; // DevSkim: ignore DS173237
    public static string CommitsSinceVersionSource = "999";
    public static string CommitsSinceVersionSourcePadded = "0999";
    public static string UncommittedChanges = "9";
    public static string CommitDate = "2024-07-27";
}
